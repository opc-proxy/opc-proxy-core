using System;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using converter;
using NLog;

namespace Tests
{

    public class nodeSelectorTest{

        Opc.Ua.Export.UAVariable node;
        nodesConfig config;

        public nodeSelectorTest(){
            node = new Opc.Ua.Export.UAVariable();
            node.BrowseName = "paul";
            config = new nodesConfig();
            config.targetIdentifier = "browseName";
            /*
            var log = new NLog.Config.LoggingConfiguration();
            var logconsole = new NLog.Targets.ColoredConsoleTarget("logconsole");
            // Rules for mapping loggers to targets            
            log.AddRule( LogLevel.Debug, LogLevel.Fatal, logconsole);
            // Apply config           
            NLog.LogManager.Configuration = log; 
            */
        }

        [Fact]
        public void configurationTarget()
        {
            NodesSelector sel = new NodesSelector(config);  // default config: target displayName

            config.targetIdentifier = "whatever";
            Assert.Throws<System.ArgumentException>( () => new NodesSelector(config)) ;

            config.targetIdentifier = "BrowSeName";
            sel = new NodesSelector(config);
        }
        [Fact]
        public void configurationSkipNoList()
        {
            NodesSelector sel = new NodesSelector(config); 
            Assert.True( sel.selectNode(node),"Skip selection on empty list does not work.");
        }
        [Fact]
        public void ThrowOnlyBlackList(){
            
            config.blackList = new List<string>{"bella","ciao"};
            Assert.Throws<System.ArgumentException>(() => new NodesSelector(config)) ;
            config.blackList = new List<string>();
            config.notContain = new List<string>{"bella","ciao"};
            Assert.Throws<System.ArgumentException>(() => new NodesSelector(config)) ;

        }
        [Fact]
        public void BlackListAndMatchAll(){
            config.matchRegEx = new List<string>{"^"};
            NodesSelector sel = new NodesSelector(config);
            config.notContain = new List<string>{"bella","ciao"};
            config.blackList = new List<string>{"lea","giorgio"};

            Assert.True( sel.selectNode(node),"Blacklist + matchRegEx all not working");
            node.BrowseName = "";
            Assert.True( sel.selectNode(node),"Blacklist + matchRegEx all not working 2");
            node.BrowseName = "ciao putin";
            Assert.False( sel.selectNode(node),"NotContainBlacklist + matchRegEx all not working");
            node.BrowseName = "lea";
            Assert.False( sel.selectNode(node),"Blacklist + matchRegEx all not working ");
        }
        [Fact]
        public void RegEx(){
            config.matchRegEx = new List<string>{"^obi","^OBI"};
            NodesSelector sel = new NodesSelector(config);
            node.BrowseName = "OBI ciao";
            Assert.True( sel.selectNode(node),"matchRegEx not working");
            node.BrowseName = "ciao";
            Assert.False( sel.selectNode(node),"cross check");
        }

        [Fact]
        public void WhiteListing(){
            config.whiteList = new List<string>{"hola","pola"};
            config.contains = new List<string>{"mubo","jumbo"};
            NodesSelector sel = new NodesSelector(config);
            Assert.False( sel.selectNode(node),"Should fail");
            node.BrowseName = "Hola";
            Assert.False( sel.selectNode(node),"white list should fail");
            node.BrowseName = "pola";
            Assert.True( sel.selectNode(node),"whitelist broken ");
            node.BrowseName = "polajumbo";
            Assert.True( sel.selectNode(node),"containslist broken ");
        }

        [Fact]
        public void DisplayName()
        {
            config.targetIdentifier = "displayName";
            config.whiteList = new List<string>{"ciao"};

            Opc.Ua.Export.LocalizedText[] txt = {new Opc.Ua.Export.LocalizedText()};
            txt[0].Value = "bella";
            NodesSelector sel = new NodesSelector(config);

            Assert.False( sel.selectNode(node),"Empty displayName should fail");
            node.DisplayName = txt;
            Assert.False( sel.selectNode(node),"Wrong displayName should fail");
            node.DisplayName[0].Value = "ciao";
            Assert.True( sel.selectNode(node),"displayName should pass");
        }


        [Fact]
        public void NodeID()
        {
            config.targetIdentifier = "NodeId";
            config.whiteList = new List<string>{"ciao"};
            NodesSelector sel = new NodesSelector(config);

            Assert.False( sel.selectNode(node),"Empty nodeId should fail");
            node.NodeId = "paul";
            Assert.False( sel.selectNode(node),"Wrong nodeId should fail");
            node.NodeId = "ciao";
            Assert.True( sel.selectNode(node),"nodeId should pass");
        }

        [Fact]
        public void ListConfigFromJson()
        {
            JObject o = JObject.Parse(@"{
                    targetIdentifier: 'nodeId',
            }");

            //JArray a = (JArray)o["nodesLoader"]["whiteList"];
            //List<string> s = a.ToObject<List<string>>();
            exp s = o.ToObject<exp>();
            //Console.WriteLine("list {0} {1}", s.whiteList[0], s.whiteList[1]);
            Console.WriteLine("list {0} ", s.whiteList.Length);

            NodesSelector sel = new NodesSelector(config);            
            node.NodeId = "paul";
            Assert.False( sel.selectNode(node),"Wrong nodeId should fail");
            node.NodeId = "ciao";
            Assert.True( sel.selectNode(node),"nodeId should pass");
        }
    }

    public class exp{
        public string[] whiteList {get;set;}
        public string targetIdentifier {get; set;}

        public exp(){
            targetIdentifier = "";
        }
    }
}