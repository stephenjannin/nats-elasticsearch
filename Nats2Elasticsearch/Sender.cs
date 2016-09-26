using Elasticsearch.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace nats2elasticsearch
{
    public class Sender
    {
        private static readonly List<string> _elasticsearch =new List<string>(new[] { "localhost:9200" });
        private static string _nats = "localhost:8222";
        private static string _natsVarzMonitoring;
        private static string _natsSubszMonitoring;
        private static string _natsRoutezMonitoring;
        private static int _sleep = 60000;
        private static bool _stopping;
        private static bool _fromService;
        private static readonly TraceSource Trace = new TraceSource("LoggerApp");

        public static void Start()
        {
            _fromService = true;
            Main(Environment.GetCommandLineArgs());
        }

        private static ElasticLowLevelClient _elasticClient;

        static void Main(string[] args)
        {
            for(int i=0;i<args.Length;i++) 
            {
                var arg = args[i];
                switch(arg.ToLowerInvariant())
                {
                    case "-elasticsearch":
                        var elasticsearch = GetNextArgumentOrEmpty(args,i);
                        _elasticsearch.Clear();
                        _elasticsearch.AddRange(elasticsearch.Split(';'));
                        break;
                    case "-nats":
                        _nats = GetNextArgumentOrEmpty(args, i);

                        break;
                    case "-sleep":
                        var sleep = GetNextArgumentOrEmpty(args, i);
                        Int32.TryParse(sleep, out _sleep);
                        break;
                }
            }

            Trace.TraceInformation("ElasticSearch = {0}",_elasticsearch);
            Trace.TraceInformation("Nats = {0}",_nats);
            Trace.TraceInformation("Sleep in Ms = {0}",_sleep);
            _natsVarzMonitoring = String.Format("http://{0}/varz",_nats);
            _natsSubszMonitoring = String.Format("http://{0}/subsz",_nats);
            _natsRoutezMonitoring = String.Format("http://{0}/routez", _nats);

            StartElasticSearchSender();
            ThreadPool.QueueUserWorkItem(SafeLoop);
            if (!_fromService)
            {
                Console.ReadKey();
                _stopping = true;
            }

        }

        public static void Stop()
        {
            _stopping = true;
        }

        private static void StartElasticSearchSender()
        {
            var nodes = new List<Uri>();
            _elasticsearch.ForEach(t => nodes.Add(new Uri(String.Format("http://{0}", t))));

            var connectionPool = new StaticConnectionPool(nodes);

            var config = new ConnectionConfiguration(connectionPool)
                //.DisableAutomaticProxyDetection()
                .EnableHttpCompression();
                //.DisableDirectStreaming(); 

            _elasticClient = new ElasticLowLevelClient(config);
        }

        private static string GetNextArgumentOrEmpty(string[] args, int i)
        {
            if (args.Length < i+1)
                return String.Empty;
            return args[i + 1];
        }

        private static async void SafeLoop(object state)
        {
            while (!_stopping)
            {
                try
                {
                    await LoopAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceInformation("SafeLoop Error {0}",ex);
                }
            }
        }



        private static async Task LoopAsync()
        {
            var varz = await Request(_natsVarzMonitoring); 
            var subsz = await Request(_natsSubszMonitoring);
            var routez = await Request(_natsRoutezMonitoring);
            ProcessJson(varz,subsz,routez);
            Thread.Sleep(_sleep);
        }

        private static async Task<string> Request(string url)
        {
            var request = WebRequest.Create(url);
            using (HttpWebResponse reponse = (HttpWebResponse)await request.GetResponseAsync())
            {
                if (reponse.StatusCode == HttpStatusCode.OK)
                {
                    using (var stream = reponse.GetResponseStream())
                    {
                        if (stream != null)
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var content = await reader.ReadToEndAsync();
                                return content;
                            }
                        }
                    }
                }
            }
            throw new ApplicationException("monitoring not found");
        }

        private static void ProcessJson(string varz,string subsz,string routez)
        {
            try
            {
                // varz
                var jsonContent = (JObject)JsonConvert.DeserializeObject(varz);
                var port = jsonContent.Value<Int32>("port");
                var gnatsdname = String.Format("gnatsd-{0}", port);
                SendToElasticSearchIndex(jsonContent, gnatsdname, "varz");

                // subsz
                var now = jsonContent.Value<DateTime>("now");
                jsonContent = (JObject)JsonConvert.DeserializeObject(subsz);
                jsonContent.Add("now", now);
                jsonContent.Add("port", port);
                SendToElasticSearchIndex(jsonContent, gnatsdname, "subsz");

                // routez
                jsonContent = (JObject)JsonConvert.DeserializeObject(routez);

                foreach (var r in jsonContent["routes"])
                {
                    var rjo = (JObject) r;
                    rjo.Add("remote_port", rjo["port"]);
                    rjo["port"] = port; // port is used to distinguish muliple gnatsd on the same server
                    rjo.Add("now", now);
                    SendToElasticSearchIndex(rjo, gnatsdname, "routez");
                }


            }

            catch (Exception ex)
            {
                Trace.TraceInformation("ProcessJson Exception {0}", ex);
            }
        }

        private static void SendToElasticSearchIndex(JObject jsonContent, string gnatsdName,string index)
        {
            jsonContent.Add("hostname", Environment.MachineName);
            jsonContent.Add("gnatsd", gnatsdName);

            var indexName = GetIndexName(index);

            PostData<object> body = new PostData<object>(jsonContent.ToString());
            var result = _elasticClient.Index<string>(indexName, index, body);
            if (!result.Success)
            {
                Trace.TraceInformation("ProcessJson ElasticClient Error {0}", result.ServerError.Error);
            }

        }

        private static string GetIndexName(string type)
        {
            return String.Format("nats{0}-{1}", type, DateTime.UtcNow.ToString("yyyy.MM.dd"));
        }
    }
}
