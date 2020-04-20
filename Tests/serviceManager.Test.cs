using System;
using System.Threading;
using Xunit;
using OpcProxyClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpcProxyCore;
using Opc.Ua;

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
                    'whiteList':['MyVariable','MyVariable2','MyVariable3']
                }
            }";

            var j = JObject.Parse(json_config);
            var s = new serviceManager(j);
            Thread t = new Thread(async () =>
            {
                Thread.Sleep(5000);
                var resp = await s.writeToOPCserver(new string[]{"MyVariable","MyVariable2","MyVariable3"}, new object[]{7,8,9});
                Assert.True(resp[0].success);
                Assert.True(resp[1].success);
                Assert.True(resp[2].success);
                Assert.Equal("MyVariable", resp[0].name);
                Assert.Equal("MyVariable2", resp[1].name);
                Assert.Equal("MyVariable3", resp[2].name);

                Assert.Equal(StatusCodes.Good, resp[0].statusCode);
                resp = await s.writeToOPCserver(new string[]{"MyVariable","pippo"}, new object[]{"ciao",8});

                Assert.Equal(StatusCodes.BadNoEntryExists, resp[1].statusCode);
                Assert.False(resp[1].success);
                Assert.Equal(StatusCodes.BadTypeMismatch, resp[0].statusCode);
                Assert.False(resp[0].success);

                s.cancellationToken.Cancel();
            });
            t.Start();
            s.run();
        }
    }
}
