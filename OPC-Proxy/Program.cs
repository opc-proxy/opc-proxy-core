using OpcProxyCore;
using Newtonsoft.Json.Linq;
using OpcProxyClient;
using OpcGrpcConnect;
using OpcInfluxConnect;


namespace OPC_Proxy
{
    class Program
    {
        static int Main(string[] args)
        {

            JObject config = JObject.Parse(
                "{isInMemory:true, filename:'pollo.dat', stopTimeout:-1, autoAccept:false, endpointURL:'opc.tcp://xeplc.physik.uzh.ch:4840/s7OPC'}"
            );
            
            
            serviceManager man = new serviceManager(config);            

            HttpImpl opcHttpConnector = new HttpImpl();
            InfluxImpl influx = new InfluxImpl();

            man.addConnector(opcHttpConnector);
            man.addConnector(influx);

            man.run();

            return (int)OPCclient.ExitCode;           

            // db.Dispose();
        }
        
    }
}
