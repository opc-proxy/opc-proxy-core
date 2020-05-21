using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using LiteDB;
using Newtonsoft.Json.Linq;
using Opc.Ua ;
using NLog;
using OpcProxyClient ;

namespace OpcProxyCore{
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
        
        public ILiteCollection<dbNode> nodes {get; private set;}
        public ILiteCollection<dbNamespace> namespaces {get;  set;}
        public ILiteCollection<dbVariableValue> latestValues {get; set;}
        public ILiteCollection<dbVariableValue> bufferValues {get; set;}
        //bool disposed = false;
        public static NLog.Logger logger = null;

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
            _config = config.ToObject<dbConfigWrapper>().nodesDatabase;
            logger = LogManager.GetLogger(this.GetType().Name);
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
            try{
                mem = new MemoryStream();

                db = (_config.isInMemory) ? new LiteDatabase(mem) : new LiteDatabase(@_config.filename) ;   
            }
            catch(Exception ex){
                logger.Error("Failed to establish Cache database");
                logger.Error("Exception: " + ex.Message);
                System.Environment.Exit(0); 
                return;
            }
            
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
        /// <param name="sub"></param>
        /// <param name="items"></param>
        //public void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e){
        public void OnNotification(object sub, MonItemNotificationArgs items){

             foreach(var itm in items.values){
                updateBuffer(items.name, itm.Value, itm.SourceTimestamp);
                logger.Debug("Updating value for {0} to {1} at {2}", items.name, itm.Value, itm.SourceTimestamp);
            }
        }


        public void insertNamespacesIfNotExist(NamespaceTable sessionNamespaceURI){

            string[] namespaces_from_table = sessionNamespaceURI.ToArray();
            for(int k=0; k< sessionNamespaceURI.Count; k++){
                dbNamespace ns = new dbNamespace {
                    internalIndex = k,
                    URI = namespaces_from_table[k],
                    currentServerIndex = k
                };
                var ns_existing = namespaces.FindOne(Query.EQ("URI", ns.URI));
                if(ns_existing == null){
                    namespaces.Insert(ns);
                }
            }
        }

        public void insertNodeIfNotExist(List<dbNode> input_nodes){
            foreach (var node in input_nodes)
            {
                insertNodeIfNotExist(node);
            }
        }

        public void insertNodeIfNotExist(dbNode input_node){
            var node_existing = nodes.FindOne(Query.EQ("name", input_node.name));
            if(node_existing == null){
                nodes.Insert(input_node);
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
            logger.Info("Number of selected nodes: " + nodes_list.Count());
            
            foreach(var node in nodes_list){
                serverNode s = new serverNode(node);
                int temp = 0;
                namespace_index_relation.TryGetValue(node.internalIndex,out temp);
                s.currentServerIndex = temp;
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
                throw new Exception("Node exist but has not related namespace");
            }
            s.currentServerIndex = ns.currentServerIndex;
            s.serverIdentifier = "ns=" + s.currentServerIndex.ToString();
            s.serverIdentifier += ";" + node.identifier;

            return s;
        }

        /// <summary>
        /// Returns the first dbNode from cache that matches the name, if no match returns null
        /// </summary>
        /// <param name="name">Name of the node to match</param>
        /// <returns></returns>
        public dbNode getDbNode(string name){
            return nodes.FindOne(Query.EQ("name", name));
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
                logger.Error("Error in updating value for variable " + name);
                logger.Error(e.Message);
            }           
        }

        /// <summary>
        /// Read a list of variables value from the DB cache given their names.
        /// </summary>
        /// <param name="names">List of names of the variables</param>
        /// <returns>Returns a list of dbVariable</returns>
        public Task<List<ReadVarResponse>> readValue( string[] names ){
            var t = Task.Run(()=>{
                BsonArray bson_arr = new BsonArray();
                foreach(string name in names ){
                    bson_arr.Add(name);
                }

                var read_var =  latestValues.Find(Query.In("name",bson_arr)).ToList();
                List<ReadVarResponse> response = new List<ReadVarResponse>();

                string l = "";
                for(int i=0; i< names.Length; i++)
                {   
                    var var_i = read_var.Find( x => x.name == names[i] );
                    if(  var_i == null) 
                    {
                        l += names[i] + ", ";
                        response.Add( new ReadVarResponse(names[i],StatusCodes.BadNoEntryExists) );
                    }
                    else response.Add( new ReadVarResponse(var_i) );
                }
                if(l !="")  { 
                    logger.Warn("Some of the varibles requested to read were not found: " + l );
                }
                return response;
            });    

            return t;        
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
    
/// <summary> just a wrapper class for the JSON structure</summary>
    public class dbConfigWrapper{
        public dbConfig nodesDatabase {get; set;}

        public dbConfigWrapper(){
            nodesDatabase = new dbConfig();
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
            value = null;
            systemType = "null";
            timestamp = DateTime.UtcNow;
        }
    }

    public class ReadVarResponse : dbVariableValue {
        public double timestampUTC_ms {
            get  {
                return timestamp.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }
        }
        public bool success {get; set;}
        public uint statusCode {get; set;}

        public ReadVarResponse(string Name, uint code):base(){
            success = false;
            name = Name;
            statusCode = code;
        }
        public ReadVarResponse(dbVariableValue v):base(){
            Id = v.Id;
            name = v.name;
            value = v.value;
            systemType = v.systemType;
            timestamp = v.timestamp;
            success = true;
            statusCode = StatusCodes.Good;
        }
    }

    public class WriteVarResponse {
        public string name {get;set;}
        public bool success {get;set;}
        public uint statusCode {get;set;}
        public object value {get;set;}
        public WriteVarResponse(string Name, object val){
            name = Name;
            success = true;
            value = val;
            statusCode = StatusCodes.Good;
        }

        public WriteVarResponse(string Name, uint code){
            name = Name;
            success = false;
            value = null;
            statusCode = code;
        }
    }
    
}