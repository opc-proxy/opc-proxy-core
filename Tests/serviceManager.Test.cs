using System;
using System.Threading;
using Xunit;
using OpcProxyClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpcProxyCore;

namespace Tests
{
    public class serviceManagerTest
    {
        [Fact]
        public void run()
        {
            string json_config = @"
            {
                'opcServerURL':'opc.tcp://localhost:4840/freeopcua/server/',
                'reconnectPeriod':10,
                'publishingInterval': 1000,
                'nodesDatabase':{
                'isInMemory':true
                },

                'loggerConfig' :{
                    'loglevel' :'debug'
                },

                'nodesLoader' : {
                    'targetIdentifier' : 'browseName',
                    'whiteList':['MyVariable']
                }
            }";

            var j = JObject.Parse(json_config);
            var s = new serviceManager(j);
            Thread t = new Thread(() =>
            {
                Thread.Sleep(5000);
                s.cancellationToken.Cancel();
            });
            t.Start();
            s.run();
        }
    }
}
