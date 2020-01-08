using System;
using Xunit;
using OpcProxyClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tests{
    public class clientTest{

       // OPCclient opc;

        [Fact]
        public void configTest()
        {
            JObject j = JObject.Parse("{opcServerURL:'something_here', reconnectPeriod: 20, publishingInterval: 5000}");
            OPCclient temp_opc = new OPCclient(j);
            
            Assert.True(temp_opc.user_config.opcServerURL == "something_here");
            Assert.True(temp_opc.user_config.reconnectPeriod == 20);
            Assert.True(temp_opc.user_config.publishingInterval == 5000);
        }

        
    }
}
