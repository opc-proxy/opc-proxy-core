using System;
using System.Threading;
using Xunit;
using OpcProxyClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpcProxyCore;

namespace Tests{
    public class serviceManagerTest{
        [Fact]
        public void run()
        {
            string json_config = @"
            {
                'endpointURL':'opc.tcp://xeplc.physik.uzh.ch:4840/s7OPC',
                'reconnectPeriod':10,
                'publishingInterval': 1000,

                'nodesDatabase':{
                    'isInMemory':true
                },
                'loggerConfig' :{
                    'loglevel' :'debug'
                },
                'nodesLoader' :{
                    'whiteList':['ciao'],
                    'browseNodes':false
                }
            }";
            var j = JObject.Parse(json_config);
            var s = new serviceManager(j);
            Thread t = new Thread(()=>{
                Thread.Sleep(5000);
//                s.cancellationToken.Cancel();
            });
            t.Start();
            s.run();
        }
    }
}