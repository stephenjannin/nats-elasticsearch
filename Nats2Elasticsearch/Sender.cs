using Elasticsearch.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Threading;

namespace nats2elasticsearch
{
    public class Sender
    {
        private static string _elasticsearch ="localhost:9200";
        private static string _nats = "localhost:8222";
        private static string _natsMonitoring;
        private static int _sleep = 1000;
        private static bool _stopping = false;
        private static bool _fromService = false;

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
                        _elasticsearch = GetNextArgumentOrEmpty(args,i);
                        break;
                    case "-nats":
                        _nats = GetNextArgumentOrEmpty(args, i);

                        break;
                    case "-sleep":
                        var sleep = GetNextArgumentOrEmpty(args, i);
                        Int32.TryParse(sleep, out _sleep);
                        break;
                    default:
                        break;
                }
            }

            Console.WriteLine("ElasticSearch = {0}",_elasticsearch);
            Console.WriteLine("Nats = {0}",_nats);
            Console.WriteLine("Sleep in Ms = {0}",_sleep);
            _natsMonitoring = String.Format("http://{0}/varz",_nats);

            StartElasticSearchSender();
            ThreadPool.QueueUserWorkItem(new WaitCallback(Loop));
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
            var node = new Uri(String.Format("http://{0}",_elasticsearch));

            var connectionPool = new StaticConnectionPool(new[] { node });

            var config = new ConnectionConfiguration(connectionPool);
                //.DisableAutomaticProxyDetection()
                //.EnableHttpCompression();
                //.DisableDirectStreaming(); 

            _elasticClient = new ElasticLowLevelClient(config);
        }

        private static string GetNextArgumentOrEmpty(string[] args, int i)
        {
            if (args.Length < i+1)
                return String.Empty;
            return args[i + 1];
        }


        private static async void Loop(object state)
        {
            while (!_stopping)
            {
                var request = WebRequest.Create(_natsMonitoring);
                using (HttpWebResponse reponse =  (HttpWebResponse) await request.GetResponseAsync())
                {
                    if (reponse.StatusCode == HttpStatusCode.OK)
                    {
  
                        using (var stream = reponse.GetResponseStream())
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var content = await reader.ReadToEndAsync();
                                ProcessJson(content);
                            }
                        }

                    }
                }

                Thread.Sleep(_sleep);
            }
        }

        private static void ProcessJson(string content)
        {
            var jsonContent = (JObject)JsonConvert.DeserializeObject(content);
            var port = jsonContent.Value<Int32>("port");
            var gnatsdname = String.Format("gnatsd-{0}", port);
            var connections = jsonContent.Value<Int32>("connections");
            jsonContent.Add("hostname", Environment.MachineName);
            jsonContent.Add("gnatsd", gnatsdname);

            PostData<object> body = new PostData<object>(jsonContent.ToString());
            var indexName = GetIndexName();
            var result =  _elasticClient.Index<string>(indexName, "nats", body);
            if (!result.Success)
            {
                Console.WriteLine(result.ServerError.Error);
            }

        }

        private static string GetIndexName()
        {

            return String.Format("natsvarz-{0}", DateTime.UtcNow.ToString("yyyy.MM.dd"));
        }
    }
}
