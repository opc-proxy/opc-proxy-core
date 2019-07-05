using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.IO;
using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua ;
using Opc.Ua.Client;
using OpcProxyClient ;

namespace ProxyUtils{
  public enum ReadStatusCode : int
    {
        Ok = 0,
        VariableNotFoundInDB = 1
        
    };


    /// <summary>
    /// Class that holds the in memory cache database. LiteDB is used as chache DB.
    /// </summary>
    public class cacheDB : Managed {
//    class cacheDB : IDisposable {
        public double p;
        public LiteDatabase db = null;
        public MemoryStream mem = null;
        
        public LiteCollection<dbNode> nodes {get; private set;}
        public LiteCollection<dbNamespace> namespaces {get;  set;}
        public LiteCollection<dbVariableValue> latestValues {get; set;}
        public LiteCollection<dbVariableValue> bufferValues {get; set;}
        //bool disposed = false;

        dbConfig _config = null;

        /// <summary>
        /// Constructor for cacheDB. config is as follows:
        /// </summary>
        /// 
        /// <para>
        ///  [isInMemory] - Boolean : if false the DB is persisted on disk (file), and can be loaded later. Warning - performance degrade. Default is true.
        /// 
        ///  [filename] - String : name of the file DB should be written to. Deafult "DBcache.opcproxy.dat" 
        /// 
        ///  [overwrite] - Boolean : if true force overwrite of DB file, false will load from file. Default false.
        /// </para>
        public cacheDB( JObject config ){
            _config = config.ToObject<dbConfig>();
            init();
        }

        private void createCollections(){

            nodes           = db.GetCollection<dbNode>("nodes");
            namespaces      = db.GetCollection<dbNamespace>("namespaces");
            latestValues    = db.GetCollection<dbVariableValue>("latestValues");
            bufferValues    = db.GetCollection<dbVariableValue>("bufferValues");

            // Creating indexes
            nodes.EnsureIndex("name");
            namespaces.EnsureIndex("URI");
            namespaces.EnsureIndex("internalIndex");
            namespaces.EnsureIndex("currentServerIndex");
            latestValues.EnsureIndex("name");
            bufferValues.EnsureIndex("name");
            bufferValues.EnsureIndex("timestamp");
        }

        private void init(){
            mem = new MemoryStream();

            db = (_config.isInMemory) ? new LiteDatabase(mem) : new LiteDatabase(@_config.filename) ;
            
            createCollections();
        }

        public void clear(){
            db.Dispose();
            mem.Dispose();
        }
        /// <summary>
        /// Drops the memory/file streams and the db and re-initialize a fresh empty db instance. 
        /// Usefull when one wants to re-load nodes in case node XML file changed.
        /// </summary>
        public void refresh(){
            clear();
            init();
        }

        /// <summary>
        /// IOPCconnect.OnNotification interface implementation see <see cref="IOPCconnect"/> for description.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="e"></param>
        //public void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e){
        public void OnNotification(object sub, MonItemNotificationArgs items){

             foreach(var itm in items.values){
                updateBuffer(items.name, itm.Value, itm.SourceTimestamp);
                logger.Debug("Updating value for {0} to {1} at {2}", items.name, itm.Value, itm.SourceTimestamp);
            }
        }


        /// <summary>
        /// Assign to previously loaded namespaces the current local index in the server
        /// </summary>
        /// <param name="sessionNamespaceURI"></param>
        public void updateNamespace(NamespaceTable sessionNamespaceURI){
            var internal_namespaces = namespaces.FindAll();
            
            foreach(var n in internal_namespaces){
                n.currentServerIndex = sessionNamespaceURI.GetIndex(n.URI);
                namespaces.Update(n);
                if(n.currentServerIndex == -1)
                {
                    logger.Warn("namespace \"" + n.URI + "\" not found in server");
                    logger.Warn("The known namespaces are: ");
                    foreach(var  ns in sessionNamespaceURI.ToArray() ){
                        logger.Warn("\t'" + ns +"'");
                    }
                }
                logger.Debug("namespace updated '"+n.URI+"' to index " + n.currentServerIndex);
            }

        }

        /// <summary>
        /// Returns a list of nodes that can be used to easily point to the current server node
        /// </summary>
        /// <returns></returns>
        public List<serverNode> getDbNodes(){
            var ns = namespaces.FindAll();
            Dictionary <int, int> namespace_index_relation = new Dictionary <int,int>{};
            foreach(var name in ns){
                namespace_index_relation.Add(name.internalIndex, name.currentServerIndex);
            }

            List<serverNode> Out = new List<serverNode>{};
            var nodes_list = nodes.FindAll();
            logger.Warn("number of nodes : " + nodes_list.Count());
            
            foreach(var node in nodes_list){
                serverNode s = new serverNode(node);
                s.currentServerIndex = namespace_index_relation.GetValueOrDefault(node.internalIndex);
                s.serverIdentifier = "ns=" + s.currentServerIndex.ToString();
                s.serverIdentifier += ";" + node.identifier;

                Out.Add(s);
            }

            return Out;
        }


        /// <summary>
        /// Return a ServerNode, the current representation in the server for that node
        /// </summary>
        /// <param name="name">Name of the variable</param>
        /// <returns></returns>
        public serverNode getServerNode(string name){
            var node = nodes.FindOne(Query.EQ("name", name));
            if(node == null){
                throw new Exception("Node does not exist");
            }
            serverNode s = new serverNode(node);
            var ns = namespaces.FindOne(Query.EQ("internalIndex", node.internalIndex));
            if(ns == null){
                throw new Exception("Node Exist but has not related namespace");
            }
            s.currentServerIndex = ns.currentServerIndex;
            s.serverIdentifier = "ns=" + s.currentServerIndex.ToString();
            s.serverIdentifier += ";" + node.identifier;

            return s;
        }

        /// <summary>
        /// Update the cache with the new value of that variable
        /// </summary>
        /// <param name="name">name of variable</param>
        /// <param name="value"> updated value of variable</param>
        /// <param name="time"> timestamp of when it changed</param>
        public void updateBuffer(string name, object value, DateTime time){
            try{
                dbVariableValue var_idx = latestValues.FindOne(Query.EQ("name",name));

                // if not found then search in nodes list
                if(var_idx == null) 
                    var_idx = _initVarValue(name);
                logger.Debug("value -> "+value.ToString() + "  type --> " + var_idx.systemType);
                var_idx.value = Convert.ChangeType(value, Type.GetType(var_idx.systemType));
                var_idx.timestamp = time;
                latestValues.Upsert(var_idx);

            } 
            catch (Exception e){
                logger.Error(e, "Error in updating value for variable " + name);
                Console.WriteLine(e.Message);
            }           
        }

        /// <summary>
        /// Read a list of variables value from the DB cache given their names.
        /// Note: this function is not async, since liteDB do not support it yet.
        /// </summary>
        /// <param name="names">List of names of the variables</param>
        /// <param name="status">Status of the transaction, "Ok" if good, else see <see cref="ReadStatusCode"/> </param>
        /// <returns>Returns a list of dbVariable</returns>
        public dbVariableValue[] readValue(string[] names, out ReadStatusCode status){
            
            BsonArray bson_arr = new BsonArray();
            foreach(string name in names ){
                bson_arr.Add(name);
            }

            dbVariableValue[] read_var =  latestValues.Find(Query.In("name",bson_arr)).ToArray();
            
            if(read_var.Count() != names.Length)  { 
                status = ReadStatusCode.VariableNotFoundInDB;

                string l = "";
                List<dbVariableValue> values = read_var.ToList();
                foreach(var v in names){
                    if( values.Find( x => x.name == v) == null) l += v + ", ";
                }
                logger.Warn("Some of the varibles requested to read were not found: " + l );
            }
            else status = ReadStatusCode.Ok;
            
            return read_var;
        }

        /// <summary>
        /// Initialization of the variable value in DB, this is used if the variable does not exist yet, 
        /// then one looks into the nodelist.
        /// </summary>
        /// <param name="name">name of the variable value to initialize</param>
        /// <returns></returns>
        private dbVariableValue _initVarValue(string name){
            dbNode var_idx = nodes.FindOne(Query.EQ("name",name));
            
            if(var_idx == null)  {
                throw new Exception("variable does not exist: "+name );
            }
            else {
                dbVariableValue new_var = new dbVariableValue {
                    Id = var_idx.Id,
                    name = var_idx.name,
                    systemType = var_idx.systemType,
                };
                return new_var;
            }
        }

    }
    
    /// <summary>
    /// Configuration handler for DBcache
    /// </summary>
    public class dbConfig{
    
        public bool isInMemory { get; set; }
        public string filename { get; set;}
        public bool overwrite{get; set;}

        public dbConfig(){
            isInMemory = true;
            filename = "DBcache.opcproxy.dat";
            overwrite = false;
        }
    }

    /// <summary>
    /// Representation of an OPC Server Node. 
    /// </summary>
    public class dbNode{
        public int Id { get; set; }
        public string name {get;set;}
        public string identifier {get;set;}
        public int internalIndex{get;set;}
        public string classType {get;set;}
        public string systemType {get;set;}
        public string[] references{get;set;}
    }

    /// <summary>
    /// Helper class that can be used to refer to a server node directly. 
    /// The 'serverIdentifier' identify the node in the current server session.
    /// </summary>
    public class serverNode : dbNode
    {
        public string serverIdentifier {get; set;}
        public int currentServerIndex {get; set;}

        public serverNode(dbNode n){
            Id = n.Id;
            name = n.name;
            identifier = n.identifier;
            internalIndex = n.internalIndex;
            classType = n.classType;
            systemType = n.systemType;
            references = n.references;
            currentServerIndex = -1;
            serverIdentifier = "none";
        }
    }


    /// <summary>
    /// Node internal and server related namespace: the nodes in the DB are stored referring to a namespaceIndex which 
    /// is genereted internally at creation time. This table holds the current server namespace 
    /// index for that URI (which can change at any new session) and the internal node index assigned 
    /// at the node insertion time (which will not change).
    /// </summary>
    public class dbNamespace{
        public int Id { get; set; }
        public int internalIndex {get; set;}
        public string URI {get;set;}
        public int currentServerIndex {get;set;}
    }

    /// <summary>
    /// Variable stored value
    /// </summary>
    public class dbVariableValue{
        public int Id { get; set; }
        public string name{get;set;}
        public object value{get;set;}
        public string systemType {get;set;}
        public DateTime timestamp {get;set;}

        public dbVariableValue(){
            Id = -9;
            name = "does_not_exist";
            value = -9;
            systemType = "null";
            timestamp = DateTime.Now;
        }
    }
}