// Copyright 2015 gRPC authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading.Tasks;
using Grpc.Core;
using OpcGrpcConnect;
using System.Collections.Generic;

using OpcProxyClient; 
using Opc.Ua; 
using OpcProxyCore;
using Newtonsoft.Json.Linq;
using NLog;


namespace OpcGrpcConnect
{
    class HttpImpl : Http.HttpBase, IOPCconnect
    {

        private serviceManager _services;
        private Server server;
        public static Logger logger = LogManager.GetCurrentClassLogger();


        // Server side handler of the SayHello RPC
        public override Task<ReadResponse> ReadOpcNodes(ReadRequest request, ServerCallContext context)
        {
            List<string> names = new List<string>{};

            foreach( var name in request.Names){
                names.Add(name);
            }
            ReadStatusCode status;
            var values =  _services.readValueFromCache(names.ToArray(),out status);

            ReadResponse r = new ReadResponse();

            foreach(var variable in values){
                NodeValue val = new NodeValue();
                val.Name = variable.name;
                val.Type = variable.systemType;
                val.Value = variable.value.ToString();
                val.Timestamp = variable.timestamp.ToUniversalTime().ToString("o");
                r.Nodes.Add(val);
            }
            r.ErrorMessage = ( status == ReadStatusCode.Ok) ? "none" : "Error";
            r.IsError = ( status != ReadStatusCode.Ok);

            return Task.FromResult( r );
        }
        public async override Task<WriteResponse> WriteOpcNode (WriteRequest request, ServerCallContext context){
            
            StatusCodeCollection statuses = await _services.writeToOPCserver(request.Name, request.Value ) ;

            WriteResponse r = new WriteResponse();
            r.IsError = (Opc.Ua.StatusCode.IsBad(statuses[0]));
            r.ErrorMessage = (r.IsError) ? "Error" : "none";

            if(r.IsError) logger.Error("Error in writing");
            else logger.Debug("Written value: " +  request.Value + "  on variable " + request.Name );
            return r;
        }


        /// <summary>
        /// Not needed here, does nothing
        /// </summary>
        /// <param name="item"></param>
        /// <param name="e"></param>
        public void OnNotification(object obj, MonItemNotificationArgs args){

        }

        /// <summary>
        /// This is to get the pointer to the service manager and have access to
        /// all it methods. One needs to store this pointer to a local variable.
        /// </summary>
        /// <param name="serv">Pointer to the current service manager</param>
        public void setServiceManager( serviceManager serv){
            _services = serv;
        }

        /// <summary>
        /// Initialization. Everything that needs to be done for initializzation will be passed here.
        /// </summary>
        /// <param name="config">JSON configuration see Newtonsoft.Json for how to parse an object out of it</param>
        public void init(JObject config){
            const int Port = 50051;
            server = new Server
            {
                Services = { Http.BindService(this) },
                Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
            };
            
            server.Start();

            logger.Info("Listening on port 50051 ...");
            //server.ShutdownAsync().Wait();

        }

    }

}
