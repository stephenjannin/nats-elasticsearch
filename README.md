# nats-elasticsearch

Send gnatsd (http://nats.io/) monitoring informations available in http://localhost:8222/varz to elasticsearch

## How to install as a windows service with command line arguments

* copy Nats2ElasticsearchService\bin\Release\*.* somewhere
* in a command line window (as administrator)

`sc create Nats2ElasticSearch-4222 start= delayed-auto binpath= "C:\Nats2ElasticSearch\Nats2ElasticSearchService.exe -elasticsearch localhost:9200 -nats localhost:8222 -sleep 60000"`

## Kibana dashboard example

Timeline of the number of connections of a nats cluster with 3 nodes

![Kibana Nats Cluster Connections](kibana.png?raw=true)
