using OpcProxyClient;

using Newtonsoft.Json.Linq;
using Opc.Ua;
using NLog;
using converter;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Threading;
using System.IO;

namespace OpcProxyCore{

    /// <summary>
    /// Class that manages the comunication between all the services, dbCache, OPCclient, TCP server, kafka server.
    /// Takes care of initialization of all services and of setting up event handlers.
    /// </summary>
    public class serviceManager {
        public cacheDB db;
        public OPCclient opc;

        private List<IOPCconnect> connector_list;

        private JObject _config;
        public static Logger logger = null;
        
        public CancellationTokenSource cancellationToken;
            
        public serviceManager(string[] args){       
            string config_file_path = "proxy_config.json";

            for(int k=0; k < args.Length; k++)  {
                if(args[k] == "--config" && args.Length > k+1) {
                    config_file_path = args[k+1];
                }                
            }
            string json = "";
            try 
            {
                using (StreamReader sr = new StreamReader(config_file_path)) 
                {
                    json = sr.ReadToEnd();
                    JObject config = JObject.Parse(json);
                    _constructor(config);
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine("An error occurred during initialization: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("\nUsage:\n  exe_name --config path_to_file\n  if --config is not specified in will look for a file called 'proxy_config.json'\n");
                System.Environment.Exit(0); 
            }
        }

        public void _init_constructor(JObject config){
            _config = config;
            init_logging(config.ToObject<logConfigWrapper>().loggerConfig);
            logger = LogManager.GetLogger(this.GetType().Name);
            opc = new OPCclient(config);
            db = new cacheDB(config);
            connector_list = new List<IOPCconnect>{};

            // setting up the comunication line back to the manager
            opc.setPointerToManager(this);
            db.setPointerToManager(this);
            connectOpcClient();
            browseNodesFillCache();
        }

        public serviceManager(JObject config){
            _constructor(config);
        }
        
        private void _constructor(JObject config){
            // setting up the cancellation Process
            cancellationToken = new CancellationTokenSource();

            try{
                _init_constructor(config);
            }
            catch (Exception e) 
            {   Console.WriteLine("An error occurred during initialization: ");
                Console.WriteLine(e.Message);
                System.Environment.Exit(0); 
            }
        }

        public void addConnector(IOPCconnect connector){
            connector_list.Add(connector);
        }
        public void initConnectors(){
            foreach(var c in connector_list){
                c.setServiceManager(this);
                c.init(_config, cancellationToken);
            }
        }

        public void connectOpcClient(){
            opc.connect();
        }

        public void run(){
            subscribeOpcNodes();
            initConnectors();
            logger.Info("Running...Press Ctrl-C to exit...");
            wait();
        }
        
        public static void init_logging(logConfig userConfig){
            // Logging 
            var config = new NLog.Config.LoggingConfiguration();
            // Targets where to log to: File and Console
            //var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "file.txt" };
            var logconsole = new NLog.Targets.ColoredConsoleTarget("logconsole");
            
            LogLevel userLogLevel = LogLevel.Info;
            if(userConfig.logLevel.ToLower() == "debug") userLogLevel = LogLevel.Debug;
            if(userConfig.logLevel.ToLower() == "warning") userLogLevel = LogLevel.Warn;
            if(userConfig.logLevel.ToLower() == "error") userLogLevel = LogLevel.Error;
            if(userConfig.logLevel.ToLower() == "fatal") userLogLevel = LogLevel.Fatal;
             
            // Rules for mapping loggers to targets            
            config.AddRule(userLogLevel, LogLevel.Fatal, logconsole);

            // Apply config           
            NLog.LogManager.Configuration = config;
        }

        /// <summary>
        /// Wait for a cancel/kill event. Supports Ctrl-C key press, Unix SIGTERM and cancel from any Connectors via cancellationToken.
        /// Insure proper cleanup after a cancel/kill event. 
        /// </summary>
        public void wait(){
            ManualResetEvent quitEvent = new ManualResetEvent(false);
            ManualResetEvent sigTerm = new ManualResetEvent(false);
            
            // First way of canceling, a CTRL+C signal from user
            Console.CancelKeyPress += (sender, eArgs) =>
            {
                logger.Info("Received close event... Waiting for cleanup process");
                // Set the canceling token true for all the thread that use it (in particular kafka)
                cancellationToken.Cancel();
                // avoid default process killing
                eArgs.Cancel = true;
            };

            // Second way to cancel: any of the Connectors can set the cancel token
            CancellationTokenRegistration registration = cancellationToken.Token.Register(()=>{
                // wait for all thread canceling side effects to take place
                Thread.Sleep(100);
                // Run the remaining cleanup functions
                quitEvent.Set();
            });

            // Third way to cancel: Unix SIGTERM
             AppDomain.CurrentDomain.ProcessExit += (s, e) => 
            {
                Console.WriteLine("Received SIGTERM...");
                cancellationToken.Cancel();
                Task t = new TaskFactory().StartNew(()=>{
                    // wait 2 sec, force quitting if cleanup not already finished by then 
                    Thread.Sleep(2000);
                    sigTerm.Set();
                });

                sigTerm.WaitOne();
            };

            // the registration to the cancel event is done after initialization of the connectors
            // so it will not fire if it is canceled during init. Here we check for that.
            if(cancellationToken.Token.IsCancellationRequested)  quitEvent.Set();

            // wait for cancel event or Ctrl-C, this is blocking the main thread
            using(registration){
                quitEvent.WaitOne();
                // call cleanup code
                cleanUpAll();
                sigTerm.Set();
            }
                  
        }

        public void cleanUpAll(){
            if(opc.session != null) opc.session.Close();
            Console.WriteLine("OPC Session closed...");
            foreach( var connector in connector_list){
                try{
                    connector.clean();
                }
                catch(Exception e){
                    Console.WriteLine("An error occurred while cleaning up: " + e.Message);
                }
            }
            Console.WriteLine("Connectors clean up completed...");
            db.db.Dispose(); // this is probably not needed
        }

         /// <summary>
        /// Read a list of variables value from the DB cache given their names.
        /// </summary>
        /// <param name="names">List of names of the variables</param>
        /// <returns>Returns a list of dbVariable</returns>
        public  Task<List<ReadVarResponse>> readValueFromCache(string[] names){
            return  db.readValue(names);
        }


        /// <summary>
        /// Gets a list of nodes from the cacheDB and subscribes to opc-server change for all of them.
        /// The OPC-server notifies that a monitored item has changed value/status, when this happen 
        /// an "Notification" event is fired on the client side. This method register the "OnNotification"
        /// event handler of all the added IOPCconnect interface to the service manager. For each item change
        /// all the event handlers are invoked, there is no filter currently.
        /// </summary>
        public void subscribeOpcNodes(){
            opc.subscribe( db.getDbNodes(), collectOnNotificationEventHandlers() );
        }

        /// <summary>
        /// Return a list of eventHandler to register to the onNotification event.
        /// This takes all the I/O interfaces and add their notificationHandler to the list.
        /// The "onNotification" event is emitted by the opc-client every time
        /// the OPC-server notifies that a monitored item has changed value/status.
        /// Each eventHandler will be attached to any monitoredItem (and so to any selected node).
        /// </summary>
        /// <returns></returns>
        private List<EventHandler<MonItemNotificationArgs>> collectOnNotificationEventHandlers(){
            
            List<EventHandler<MonItemNotificationArgs>> t = new List<EventHandler<MonItemNotificationArgs>>{};
            t.Add(db.OnNotification);

            foreach( var connector in connector_list){
                t.Add(connector.OnNotification);
            }

            return t;
        }

        public void browseNodesFillCache(){
            
            bool browseNodes = _config.ToObject<nodesConfigWrapper>().nodesLoader.browseNodes;
            // filling nodes from XML nodes list   
            if(browseNodes == false){
                logger.Info("Loading nodes from XML File...");
                UANodeConverter ua = new UANodeConverter(_config, opc.session.NamespaceUris);
                ua.fillCacheDB(db);
            }
            else {
                // filling nodes via browse feature
                logger.Info("Loading nodes via browsing the OPC server...");
                db.insertNamespacesIfNotExist(opc.session.NamespaceUris);
                db.insertNodeIfNotExist(opc.surf());
            }

        }

        /// <summary>
        /// Write Asyncronously to the OPC server the variable specified and its value.
        /// Takes care to do value conversion to the correct server type for the variable. 
        /// NOTE: that it throws if var_name.Length != in_values.Length, so this function call
        /// must be always within a try block.
        /// </summary>
        /// <param name="var_name">Display Name of the variable to write to</param>
        /// <param name="in_values">Value to write</param>
        /// <returns></returns>
        public async Task<List<WriteVarResponse>> writeToOPCserver(string[] var_name, object[] in_values){
            
            if(var_name.Length != in_values.Length) throw new Exception("Names and Values must be arrays of same size.");
            var s_nodes = new List<serverNode>();
            var values = new List<object>();
            var bad_responses = new List<WriteVarResponse>();

            for(uint k=0; k< var_name.Length; k++ )
            {
                try{
                    var node = db.getServerNode(var_name[k]) ;
                    var val = Convert.ChangeType(in_values[k], Type.GetType(node.systemType));
                    values.Add( val );
                    s_nodes.Add( node );
                }
                catch(Exception e)
                {
                    if(e.Message.ToLower().Contains("exist"))
                    {
                        logger.Error("Write failed. Variable \""+var_name[k] +"\" does not exist in cache DB.");
                        bad_responses.Add( new WriteVarResponse(var_name[k], StatusCodes.BadNoEntryExists) );
                    } 
                    else 
                    {
                        logger.Error("Write failed. Variable \""+var_name[k] +"\" Value type not compatible.");
                        bad_responses.Add( new WriteVarResponse(var_name[k], StatusCodes.BadTypeMismatch) );
                    }
                }
            }
            var response = new List<WriteVarResponse>();
            if(values.Count != 0 && s_nodes.Count != 0 ) response = await opc.asyncWrite(s_nodes, values);

            foreach (var item in response)
            {
                if(item.success) db.updateBuffer(item.name, item.value, DateTime.UtcNow );
            }
            response.AddRange(bad_responses);
            return response;
        }

        public JObject getRawConfig(){
            return _config;
        }
    }

    public class logConfigWrapper{
        public logConfig loggerConfig {get; set;}
        public logConfigWrapper(){
            loggerConfig = new logConfig();
        }
    }
    public class logConfig{
        public string logLevel {get; set;}
        public logConfig(){
            logLevel = "info";
        }
    }

    public class Managed {
        public serviceManager _serviceManager;

        public Managed(){
            _serviceManager = null;
        }

        public void setPointerToManager(serviceManager ser){
            _serviceManager = ser;
        }
    }

    public class logged{
        public static Logger logger = null;
        public logged(){
            logger = LogManager.GetLogger(this.GetType().Name);
        } 

    }

    /// <summary>
    /// Inteface to connect any service to the OPC-Proxy core stack.
    /// Need to add the following dependencies: OpcProxyClient, Newtonsoft.Json.Linq, Opc.Ua, ProxyUtils
    /// </summary>
    public interface IOPCconnect{
        /// <summary>
        /// Event Handler for the subscribed MonitoredItem (node), this will be attached to all monitored nodes.
        /// </summary>
        /// <param name="emitter">The ProxyClient that has emitted the event, you don't need this variable.</param>
        /// <param name="items">List of received values (it can be more than one if there are connection hiccups due to opc serverside batching).
        /// see MonItemNotificationArgs </param>
        void OnNotification(object emitter, MonItemNotificationArgs items);

        /// <summary>
        /// This is to get the pointer to the service manager and have access to
        /// all its methods. One needs to store this pointer to a local variable.
        /// </summary>
        /// <param name="serv">Pointer to the current service manager</param>
        void setServiceManager( serviceManager serv);

        /// <summary>
        /// Initialization. Everything that needs to be done for initializzation will be passed here.
        /// </summary>
        /// <param name="config">JSON configuration see Newtonsoft.Json for how to parse an object out of it</param>
        /// <param name="cts">Cancellation Token Source, to be able to gracefully shutdown in case of errors.</param>
        void init(JObject config, CancellationTokenSource cts);
        
        /// <summary>
        /// Cleanup function to be called if there is any particular clean up to do to close the application gracefully
        /// </summary>
        void clean();
    }
}