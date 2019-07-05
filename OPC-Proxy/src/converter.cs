using Opc.Ua ;
using Opc.Ua.Export;
using System;
using System.Collections.Generic;
using System.IO;

using ProxyUtils;
using NLog;

namespace converter {

    public class UANodeConverter : logged{
        
        Opc.Ua.Export.UANodeSet m_UANodeset ;
        Opc.Ua.Export.NodeIdAlias[] m_aliases;
        string[] m_namespaceURIs;
        List<Node> out_nodeset;
        NamespaceTable session_namespace;

        public UANodeConverter(string filename, NamespaceTable SessionNamespaceURIs){

            using (Stream stream = new FileStream(filename, FileMode.Open)){
                m_UANodeset = UANodeSet.Read(stream);
                m_aliases = m_UANodeset.Aliases;
                m_namespaceURIs = m_UANodeset.NamespaceUris;
                out_nodeset = new List<Node>();
                session_namespace = SessionNamespaceURIs;
            }
            
        }

        string getNodeNamespace(string id){
            //logger.Debug("get namespace -> " + id );

            UInt32 uri_index = Convert.ToUInt32((id.Split(";")[0]).Substring(3),10) - 1;

            if(uri_index >=m_namespaceURIs.Length  ) 
                logger.Debug("out of range -- " + id + "   index " + uri_index.ToString());
            return m_namespaceURIs[uri_index];
        }
        uint getNodeNamespaceIndex(string id){
            UInt32 uri_index = Convert.ToUInt32((id.Split(";")[0]).Substring(3),10) - 1;
            return uri_index;
        }

        object getIdentifier( string id){
            //logger.Debug("get Id -> " + id );
            
            Boolean isNum = false;
            string id_str = "";

            // case of only identifier no name space
            if(id.Split(";").Length == 1){
                isNum = (id[0] == 'i');
                id_str = id.Substring(2);
            }
            // case with namespace
            else {
                isNum = (id.Split(";")[1][0] == 'i');
                id_str = (id.Split(";")[1]).Substring(2);
            }

            object identifier = (isNum) ? ((object)Convert.ToUInt32(id_str,10) ): ((object)id_str);
            return identifier;
        }
        
        string getIdentifierToString( string id){
            
            string id_str = "";
            // case of only identifier no name space
            if(id.Split(";").Length == 1){
                id_str = id;
            }
            // case with namespace
            else {
                id_str = (id.Split(";")[1]);
            }
            return id_str;
        }
        NodeId get_dataType(UAVariable var){
            
            // check first in the aliases
            foreach(NodeIdAlias alias in m_aliases) {
                    logger.Debug("------ Substring ----- " + alias.Value + "  sub-> " );//+alias.Value.Substring(2));

                if(alias.Alias == var.DataType) {
                    logger.Debug("------ Matched with " + var.DataType );//+alias.Value.Substring(2));

                    // case of non built it dataType alias
                    if(alias.Value.Split(";").Length > 1) {
                        return NodeId.Create(
                            getIdentifier(alias.Value),
                            getNodeNamespace(alias.Value),
                            session_namespace
                            );
                    }
                    // case of built in DataType alias
                    else return new NodeId((uint)getIdentifier(alias.Value));
                }
            }
            logger.Debug("Not in Aliases " + var.DataType);
            // Check if is a nodeID
            if( var.DataType.Substring(0,2) == "i=" ||var.DataType.Substring(0,3) == "ns=" ){
                logger.Debug("nodeID in dataType " + var.DataType);
                return NodeId.Create(
                    getIdentifier(var.DataType),
                    getNodeNamespace(var.DataType),
                    session_namespace
                );
            }
            // then try with system types
            if(Type.GetType(var.DataType) != null) 
                return TypeInfo.GetDataTypeId(Type.GetType(var.DataType));

            else return NodeId.Null;
        }
        string get_systemDataType(UAVariable var){
            
            // check first in the aliases
            foreach(NodeIdAlias alias in m_aliases) {
                if(alias.Alias == var.DataType) {
                    // case of non built in dataType alias
                    if(alias.Value.Split(";").Length > 1) {
                        NodeId n =  NodeId.Create(
                            getIdentifier(alias.Value),
                            getNodeNamespace(alias.Value),
                            session_namespace
                            );
                        logger.Debug("Non built in data type " + var.DataType);
                        logger.Debug("Trying interpreting as built in data type " + alias.Value);
                        BuiltInType b = TypeInfo.GetBuiltInType( n );
                        if(b == BuiltInType.Null) {
                            logger.Debug("Not found type " + var.DataType + " skipping variable " + var.BrowseName);
                            return "null";
                        }
                        return TypeInfo.GetSystemType(b, var.ValueRank).ToString();
                        //return TypeInfo.GetSystemType( n,  EncodeableFactory.GlobalFactory).ToString();
                    }
                    // case of built in DataType alias
                    else {
                        logger.Debug("Alias for " + var.BrowseName + " is a built in data type " + var.DataType);

                        NodeId n = new NodeId((uint)getIdentifier(alias.Value));
                        BuiltInType b = TypeInfo.GetBuiltInType( n );
                        return TypeInfo.GetSystemType(b, var.ValueRank).ToString();
                    }
                }
            }
            logger.Debug("Not in Aliases " + var.DataType);
            // Check if is a nodeID
            if( var.DataType.Substring(0,2) == "i=" ||var.DataType.Substring(0,3) == "ns=" ){
                logger.Debug("nodeID in dataType " + var.DataType);
                NodeId n = NodeId.Create(
                    getIdentifier(var.DataType),
                    getNodeNamespace(var.DataType),
                    session_namespace
                );
                BuiltInType b = TypeInfo.GetBuiltInType( n );
                return TypeInfo.GetSystemType(b, var.ValueRank).ToString();
            }
            // then try with system types
            if(Type.GetType(var.DataType) != null) 
                return Type.GetType(var.DataType).ToString();

            else return "null";
        }

        public List<Node> convertToLocalNodeset(){

            foreach ( Opc.Ua.Export.UANode node in m_UANodeset.Items){

                if(node.BrowseName != "ciao" && (node.DisplayName[0].Value != "ciao")) continue;
                    
                if(node.GetType().ToString() == "Opc.Ua.Export.UAVariable"){

                    Opc.Ua.Export.UAVariable xml_node = node as UAVariable;

                    // creating the variableNode
                    VariableNode v_node =  new VariableNode();
                    v_node.NodeClass = NodeClass.Variable;

                    // Assign NodeID
                    v_node.NodeId = NodeId.Create(
                        getIdentifier(xml_node.NodeId),
                        getNodeNamespace(xml_node.NodeId),
                        session_namespace
                    );
                    // Assign data type
                    v_node.DataType = get_dataType(xml_node);

                    // Assign Rank and other
                    v_node.ValueRank   = xml_node.ValueRank;
                    v_node.BrowseName  = node.BrowseName;
                    if(xml_node.DisplayName != null && xml_node.DisplayName[0] != null) v_node.DisplayName = xml_node.DisplayName[0].Value;
                    if(xml_node.Description != null) v_node.Description = xml_node.Description.ToString();

                    out_nodeset.Add(v_node);
                }

            }
            return out_nodeset;
        }

        public void fillCacheDB( cacheDB db){
            if(m_UANodeset == null || m_UANodeset.Items.Length == 0 ) throw new Exception("No UA nodes loaded");

            // fill the namespace in DB
            logger.Debug("filling cache DB ");
            for( int k =0; k < m_namespaceURIs.Length; k++) {
                dbNamespace ns = new dbNamespace {
                    internalIndex = k,
                    URI = m_namespaceURIs[k],
                    currentServerIndex = -1
                };
                logger.Debug("filling cache --- inserting : " + k);

                db.namespaces.Insert(ns);

            }
            logger.Debug("filling cache DB End ");

            // connect the server index to the corresponding xml_index
            db.updateNamespace(session_namespace);
            
            foreach ( Opc.Ua.Export.UANode node in m_UANodeset.Items){

                if(node.GetType().ToString() == "Opc.Ua.Export.UAVariable"){

                    bool skip = false;

                    Opc.Ua.Export.UAVariable xml_node = node as UAVariable;

                    // creating the variableNode
                    dbNode db_node =  new dbNode();
                    db_node.classType = node.GetType().ToString();

                    // Assign NodeID
                    db_node.identifier = getIdentifierToString(xml_node.NodeId) ;
                    // Assign data type
                    db_node.systemType = get_systemDataType(xml_node);
                    // Assign internal index
                    db_node.internalIndex = ((int)getNodeNamespaceIndex(xml_node.NodeId));

                    // Assign Rank and other
                    // FIXME
                    //db_node.ValueRank   = xml_node.ValueRank;
                    if(xml_node.ValueRank > 1 ) {
                        skip = true;
                        logger.Error("Arrays are not supported yet. Skip Node: " + node.BrowseName);
                    }
                    // skipping not built in types... FIXME
                    if( !db_node.systemType.StartsWith("System") ){
                        skip = true;
                        logger.Error("Only System types are supported for now. Skip Node: " + node.BrowseName + "  type: " + db_node.systemType );
                    }
                    if(xml_node.DisplayName != null && xml_node.DisplayName[0] != null)
                        db_node.name  = node.DisplayName[0].Value;
                    else {
                        db_node.name = node.BrowseName;
                        logger.Warn("Node: " + node.BrowseName + "  does not have DisplayName, using browseName instead");
                    }

                    if(db_node.systemType.ToLower() != "null" && !skip)
                        db.nodes.Insert(db_node);
                }

            }

        }
    }

    public class typeConverter {
        
        Dictionary <string,BuiltInType> map ;
        
        public typeConverter(){
            map = new Dictionary<string,BuiltInType>();
            map.Add("Boolean",BuiltInType.Boolean);
            map.Add("BOOL",BuiltInType.Boolean);
            map.Add("SByte",BuiltInType.SByte);
            map.Add("Byte",BuiltInType.Byte);
            map.Add("UInt16",BuiltInType.UInt16);
            map.Add("UInt32",BuiltInType.UInt32);
            map.Add("UInt64",BuiltInType.UInt64);
            map.Add("Integer",BuiltInType.Integer);
            map.Add("UInteger",BuiltInType.UInteger);
            map.Add("Float",BuiltInType.Float);
            map.Add("Double",BuiltInType.Double);
            map.Add("DataValue",BuiltInType.DataValue);
            map.Add("DateTime",BuiltInType.DateTime);
            map.Add("Null",BuiltInType.Null);
            map.Add("INT",BuiltInType.Int16);
            map.Add("Int16",BuiltInType.Int16);
            map.Add("Int32",BuiltInType.Int32);
            map.Add("String",BuiltInType.String);
            map.Add("LocalizedText",BuiltInType.LocalizedText);
            map.Add("Enumeration",BuiltInType.Enumeration);
        }

       public object convert(object value, Opc.Ua.Export.UANode node){
            if( node.GetType().ToString() == "Opc.Ua.Export.UAVariable") {
                Opc.Ua.Export.UAVariable m_node= node as Opc.Ua.Export.UAVariable;
                Type t = TypeInfo.GetSystemType(map[m_node.DataType],ValueRanks.Scalar);
                return Convert.ChangeType(value, t);
            }
            else return null;
        }
    }
}