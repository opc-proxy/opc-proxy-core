using System;
using Xunit;
using OpcProxyCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LiteDB;
using System.Collections.Generic;
using System.Linq;
using converter;
using Opc.Ua ;

namespace Tests
{

    public class cacheDBTest
    {
        JObject j;
        cacheDB cDB;

        public cacheDBTest(){
            string json_config = @"{
                nodesDatabase:{
                    isInMemory:true, filename:'pollo.dat', juno:'bul'
                }, 
                nodesLoader:{
                    filename:'nodeset.xml'
                }
            }";
            j = JObject.Parse(json_config);
            cDB = new cacheDB(j);

            Opc.Ua.NamespaceTable nt = new Opc.Ua.NamespaceTable();
            nt.Append("http://www.siemens.com/simatic-s7-opcua");
            UANodeConverter ua = new UANodeConverter(j, nt);
            ua.fillCacheDB(cDB);

        }

        [Fact]
        public void dbExist()
        {
            Assert.NotNull(cDB);
        }

        [Fact]
        public void loadNodesInchacheDB(){
            
            Assert.True(cDB.nodes.Count() >0);

        }

        [Fact]
        public void fillDBWithNewVar(){
            cDB.updateBuffer("ciao",72,DateTime.UtcNow);
            var q = cDB.latestValues.FindOne(Query.EQ("name","ciao"));
            Assert.NotNull(q);
            Assert.Equal(72, q.value);

        }

        [Fact]
        public async void readFromDB(){
            cDB.updateBuffer("ciao",72,DateTime.UtcNow);
            
            var q = await cDB.readValue( (new string[] {"ciao"}));
            Assert.Single(q);
            Assert.Equal(72, q[0].value);
            Assert.Equal(DateTime.UtcNow.Second, q[0].timestamp.Second);
            Assert.True(q[0].success);
            Assert.Equal(StatusCodes.Good, q[0].statusCode);

            var p = await cDB.readValue((new string[] {"ciao1"}));
            Assert.Single(p);
            Assert.False(p[0].success);
            Assert.Equal(StatusCodes.BadNoEntryExists, p[0].statusCode);
        }
    }
}
