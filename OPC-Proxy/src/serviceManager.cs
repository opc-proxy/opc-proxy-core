using OpcProxyClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using NLog;
using converter;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

using System.Timers;

namespace ProxyUtils{

    /// <summary>
    /// Class that manages the comunication between all the services, dbCache, OPCclient, TCP server, kafka server.
    /// Takes care of initialization of all services and of setting up event handlers.
    /// </summary>
    public class serviceManager : logged{
        private cacheDB db;
        private OPCclient opc;

        private List<IOPCconnect> connector_list;

        private JObject _config;
        
        public serviceManager(JObject config){
            
            _config = config;

            opc = new OPCclient(config);
            db = new cacheDB(config);

            connector_list = new List<IOPCconnect>{};

            // setting up the comunication line back to the manager
            opc.setPointerToManager(this);
            db.setPointerToManager(this);
        }
        
        public void addConnector(IOPCconnect connector){
            connector_list.Add(connector);
        }
        public void initConnectors(){
            foreach(var c in connector_list){
                c.setServiceManager(this);
                c.init(_config);
            }
        }

        public void connectOpcClient(){
            opc.connect();
        }

         /// <summary>
        /// Read a list of variables value from the DB cache given their names.
        /// Note: this function is not async, since liteDB do not support it yet.
        /// </summary>
        /// <param name="names">List of names of the variables</param>
        /// <param name="status">Status of the transaction, "Ok" if good, else see <see cref="ReadStatusCode"/> </param>
        /// <returns>Returns a list of dbVariable</returns>
        public dbVariableValue[] readValueFromCache(string[] names, out ReadStatusCode status){
            return db.readValue(names, out status);
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
            
            UANodeConverter ua = new UANodeConverter("nodeset.xml", opc.session.NamespaceUris);
            ua.fillCacheDB(db);
        }

        /// <summary>
        /// Write Asyncronously to the OPC server the variable specified and its value.
        /// Takes care to do value conversion to the correct server type for the variable. 
        /// </summary>
        /// <param name="var_name">Display Name of the variable to write to</param>
        /// <param name="value">Value to write</param>
        /// <returns></returns>
        public Task<StatusCodeCollection> writeToOPCserver(string var_name, object value){
            
            serverNode s_node;

            try{
                s_node = db.getServerNode(var_name);
            }
            catch{
                logger.Error("Write failed. Variable \""+var_name+"\" does not exist in cache DB.");
                return opc.badStatusCall();
            }

            return opc.asyncWrite(s_node, value);
        }

    }


    public class Managed : logged {
        private serviceManager _serviceManager;

        public Managed(){
            _serviceManager = null;
        }

        public void setPointerToManager(serviceManager ser){
            _serviceManager = ser;
        }
    }

    public class logged{
        public static Logger logger = LogManager.GetCurrentClassLogger();
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
        /// all it methods. One needs to store this pointer to a local variable.
        /// </summary>
        /// <param name="serv">Pointer to the current service manager</param>
        void setServiceManager( serviceManager serv);

        /// <summary>
        /// Initialization. Everything that needs to be done for initializzation will be passed here.
        /// </summary>
        /// <param name="config">JSON configuration see Newtonsoft.Json for how to parse an object out of it</param>
        void init(JObject config);
    }
}