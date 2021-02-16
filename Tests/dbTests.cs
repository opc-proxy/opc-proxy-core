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
using System.Diagnostics;
using System.Threading.Tasks;
using System.Globalization;

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
            cDB.updateBuffer("ciao",72,DateTime.UtcNow, StatusCodes.Good);
            var q = cDB.latestValues.FindOne(Query.EQ("name","ciao"));
            Assert.NotNull(q);
            Assert.Equal(72, q.value);
        }

        [Fact]
        public void UpdateBufferDoesNotCreateNewEntryIfExist()
        {
            cDB.updateBuffer("ciao",73,DateTime.UtcNow, StatusCodes.Good);   
            var latest_values = cDB.latestValues.FindAll();
            Assert.Equal(1, latest_values.Count());
            
            cDB.updateBuffer("ciao",74,DateTime.UtcNow, StatusCodes.Good);
            latest_values = cDB.latestValues.FindAll();
            Assert.Equal(1,latest_values.Count());

        }

        [Fact]
        public async void memLeakCheck()
        {
            var mem = GetMemoryUsage();
            
            for(int k=0; k< 10000; k++){
                cDB.updateBuffer("ciao",73,DateTime.UtcNow, StatusCodes.Good);
                cDB.updateBuffer("ciao",74,DateTime.UtcNow, StatusCodes.Good);
            }

            var mem2 = GetMemoryUsage();
            Assert.InRange(mem2/mem, 1.0, 1.05);
        }
        
        public float GetMemoryUsage()
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "/bin/bash";
            p.StartInfo.Arguments = "/home/pan/work/dotNet/OpcProxyProject/opc-core/Tests/process_mem.sh";
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            var l_o = output.Split("\n");
            float mem = 0;
            foreach (var item in l_o)
            {
               if(String.IsNullOrEmpty(item)|| String.IsNullOrWhiteSpace(item)) continue;
               mem = mem + float.Parse(item.Trim(),CultureInfo.InvariantCulture);
            }
            return mem;
        }

        [Fact]
        public async void readFromDB(){
            cDB.updateBuffer("ciao",72,DateTime.UtcNow, StatusCodes.Good);
            
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
