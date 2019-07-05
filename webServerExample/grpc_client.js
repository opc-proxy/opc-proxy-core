/*
 *
 * Copyright 2015 gRPC authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

var PROTO_PATH = __dirname + '/../OPC-Proxy/opc.grpc.connect.proto';

var grpc = require('grpc');
var protoLoader = require('@grpc/proto-loader');
const grpc_promise = require('grpc-promise');;

var packageDefinition = protoLoader.loadSync(
    PROTO_PATH,
    {keepCase: true,
     longs: String,
     enums: String,
     defaults: true,
     oneofs: true,

    });

var grpc_connect = grpc.loadPackageDefinition(packageDefinition).OpcGrpcConnect;

async function main() {

  var client = new grpc_connect.Http('localhost:50051', grpc.credentials.createInsecure());

  grpc_promise.promisifyAll(client);
  
  client.ReadOpcNodes()
    .sendMessage({ names: ["ciao"] })
    .then(response => {
      console.log('Greeting:', response);
      var unixTimeZero = Date.parse( response.nodes[0].timestamp )
      console.log(unixTimeZero)
    })
    .catch(err => {
      console.log('Greeting:', err);
    });

  var resp = await client.WriteOpcNode().sendMessage({name : "ciao", value : "56" });
  console.log('Greeting:', resp);
  
}


main();
