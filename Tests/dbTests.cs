using System;
using Xunit;
using ProxyUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LiteDB;
using System.Collections.Generic;
using System.Linq;
using converter;

namespace Tests
{

    public class cacheDBTest
    {
        JObject j;
        cacheDB cDB;

        public cacheDBTest(){
            j = JObject.Parse("{isInMemory:true, filename:'pollo.dat', juno:'bul'}");
            cDB = new cacheDB(j);

            Opc.Ua.NamespaceTable nt = new Opc.Ua.NamespaceTable();
            nt.Append("http://www.siemens.com/simatic-s7-opcua");
            UANodeConverter ua = new UANodeConverter("nodeset.xml", nt);
            ua.fillCacheDB(cDB);

        }

        [Fact]
        public void dbExist()
        {
            Assert.NotNull(cDB);
        }

        [Fact]
        public void loadNodesInchacheDB(){
            
            Assert.Equal(22, cDB.nodes.Count());

        }

        [Fact]
        public void fillDBWithNewVar(){
            cDB.updateBuffer("ciao",72,DateTime.Now);
            var q = cDB.latestValues.FindOne(Query.EQ("name","ciao"));
            Assert.NotNull(q);
            Assert.Equal(72, q.value);

        }

        [Fact]
        public void readFromDB(){
            cDB.updateBuffer("ciao",72,DateTime.Now);
            ReadStatusCode s;
            
            var q = cDB.readValue( (new string[] {"ciao"}), out s);
            Assert.Equal(1, q.Length );
            Assert.Equal(72, q[0].value);
            Assert.Equal(DateTime.Now.Second, q[0].timestamp.Second);
            Assert.Equal(ReadStatusCode.Ok, s);

            var p = cDB.readValue((new string[] {"ciao1"}), out s);
            Assert.Equal(0, p.Length);
            Assert.Equal(ReadStatusCode.VariableNotFoundInDB, s);
        }
    }
}
