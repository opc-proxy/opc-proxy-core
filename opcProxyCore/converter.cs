using Opc.Ua ;
using Opc.Ua.Export;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpcProxyCore;
using System.Text.RegularExpressions;

namespace converter {

    public class UANodeConverter : logged{
        
        Opc.Ua.Export.UANodeSet m_UANodeset ;
        Opc.Ua.Export.NodeIdAlias[] m_aliases;
        string[] m_namespaceURIs;
        List<Node> out_nodeset;
        NamespaceTable session_namespace;
        nodesConfig _config;
        NodesSelector selector;

        public UANodeConverter(JObject config, NamespaceTable SessionNamespaceURIs){

            _config = config.ToObject<nodesConfigWrapper>().nodesLoader;

            using (Stream stream = new FileStream(_config.filename, FileMode.Open)){
                m_UANodeset = UANodeSet.Read(stream);
                m_aliases = m_UANodeset.Aliases;
                m_namespaceURIs = m_UANodeset.NamespaceUris;
                out_nodeset = new List<Node>();
                session_namespace = SessionNamespaceURIs;
            }
            
            selector = new NodesSelector(_config);
            
        }

        string getNodeNamespace(string id){
            //logger.Debug("get namespace -> " + id );

            UInt32 uri_index = Convert.ToUInt32((id.Split(';')[0]).Substring(3),10) - 1;

            if(uri_index >=m_namespaceURIs.Length  ) 
                logger.Debug("out of range -- " + id + "   index " + uri_index.ToString());
            return m_namespaceURIs[uri_index];
        }
        uint getNodeNamespaceIndex(string id){
            UInt32 uri_index = Convert.ToUInt32((id.Split(';')[0]).Substring(3),10) - 1;
            return uri_index;
        }

        object getIdentifier( string id){
            //logger.Debug("get Id -> " + id );
            
            Boolean isNum = false;
            string id_str = "";

            // case of only identifier no name space
            if(id.Split(';').Length == 1){
                isNum = (id[0] == 'i');
                id_str = id.Substring(2);
            }
            // case with namespace
            else {
                isNum = (id.Split(';')[1][0] == 'i');
                id_str = (id.Split(';')[1]).Substring(2);
            }

            object identifier = (isNum) ? ((object)Convert.ToUInt32(id_str,10) ): ((object)id_str);
            return identifier;
        }
        
        string getIdentifierToString( string id){
            
            string id_str = "";
            // case of only identifier no name space
            if(id.Split(';').Length == 1){
                id_str = id;
            }
            // case with namespace
            else {
                id_str = (id.Split(';')[1]);
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
                    if(alias.Value.Split(';').Length > 1) {
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
                    if(alias.Value.Split(';').Length > 1) {
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
            // FIXME - possibly this function can be deleted

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

            foreach (Opc.Ua.Export.UANode node in m_UANodeset.Items)
            {

                if (node.GetType().ToString() != "Opc.Ua.Export.UAVariable") continue;

                Opc.Ua.Export.UAVariable xml_node = node as UAVariable;

                // Apply userdefined matching criteria
                if (!selector.selectNode(xml_node)) continue;

                // creating the variableNode
                dbNode db_node = new dbNode();
                db_node.classType = node.GetType().ToString();

                // Assign NodeID
                db_node.identifier = getIdentifierToString(xml_node.NodeId);
                // Assign data type
                db_node.systemType = get_systemDataType(xml_node);
                // Assign internal index
                db_node.internalIndex = ((int)getNodeNamespaceIndex(xml_node.NodeId));

                // Assign Rank and other
                // FIXME
                //db_node.ValueRank   = xml_node.ValueRank;
                if (xml_node.ValueRank > 1)
                {
                    logger.Error("Arrays are not supported yet. Skip Node: " + node.BrowseName);
                    continue;
                }
                // skipping not built in types... FIXME
                if (!db_node.systemType.StartsWith("System") || db_node.systemType.ToLower() == "null")
                {
                    logger.Error("Only System types are supported for now. Skip Node: " + node.BrowseName + "  type: " + db_node.systemType);
                    continue;
                }

                // assign user defined target name
                switch(_config.targetIdentifier.ToLower()){
                    case "displayname":
                        if (xml_node.DisplayName != null && xml_node.DisplayName[0] != null)
                            db_node.name = node.DisplayName[0].Value;
                        else
                        {
                        logger.Error("Node: " + node.BrowseName + "  does not have DisplayName, using browseName instead");
                        db_node.name = node.BrowseName;
                        }
                        break;
                    case "browsename":
                        db_node.name = node.BrowseName;
                        break;
                    case "nodeid":
                        db_node.name = node.NodeId;
                        break;
                    default:
                        logger.Fatal("This should not happen, targetID = {0}",_config.targetIdentifier.ToLower());
                        throw new Exception("targetID not allowed");
                }

                // Adding node to cache DB
                db.insertNodeIfNotExist(db_node);


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


/// <summary> Just a class wrapper to simulate the JSON hierarchy in the configuration file.</summary>
public class nodesConfigWrapper{
    public nodesConfig nodesLoader {get; set;}

    public nodesConfigWrapper(){
        nodesLoader = new nodesConfig();
    }
}
/// <summary>
/// Configuartion class for the node loader, converting xml to opc nodes.
/// </summary>
public class nodesConfig{
    /// <summary> XML file name where to get server node definitions </summary>
    public string filename {get; set;}
    public bool browseNodes {get; set;}
    public string targetIdentifier {get; set;}
    public string[] whiteList{get;set;}
    public string[] blackList{get;set;}
    public string[] contains{get;set;}
    public string[] notContain{get;set;}
    public string[] matchRegEx{get;set;}

    public nodesConfig(){
        filename = "nodeset.xml";
        targetIdentifier = "DisplayName";
        browseNodes = true;
        whiteList = Array.Empty<string>();
        blackList = Array.Empty<string>();
        contains = Array.Empty<string>();
        notContain = Array.Empty<string>();
        matchRegEx = Array.Empty<string>();
    }
}


/// <summary>
/// Class that applies node selection criteria
/// </summary>
public class NodesSelector:logged{
    nodesConfig _config;
    List<string> allowedTargets;
    Boolean skipSelection;
    public NodesSelector(nodesConfig config){
        _config = config;
        allowedTargets = new List<string>{"displayname","browsename","nodeid"}; 
        if(!allowedTargets.Contains(_config.targetIdentifier.ToLower())) {
            logger.Fatal("Target identifier '{0}' is not supported", _config.targetIdentifier);
            logger.Info("Possible target identifier are [{0}] (case insensitive)",string.Join(",",allowedTargets));
            throw new System.ArgumentException("Target identifier");
        }

        skipSelection = false;
        if( _config.whiteList.Length == 0 && 
            _config.blackList.Length == 0 && 
            _config.contains.Length == 0 && 
            _config.matchRegEx.Length == 0 &&
            _config.notContain.Length == 0) skipSelection = true;
        
        if(_config.blackList.Length != 0 && 
            _config.whiteList.Length == 0 && 
            _config.contains.Length == 0 && 
            _config.matchRegEx.Length == 0 ) 
            throw new System.ArgumentException("Black list must be used with other lists, to obtain all nodes except backlisted add to configfile ->  matchRegEx : ['^'] ");
                    
        if(_config.notContain.Length != 0 && 
            _config.whiteList.Length == 0 && 
            _config.contains.Length == 0 && 
            _config.matchRegEx.Length == 0 )
            throw new System.ArgumentException("Black lists must be used with other lists, to obtain all nodes except backlisted add to configfile ->  matchRegEx : ['^'] ");
    }

    /// <summary>
    /// Selects the provided node against the selection rules in the node config. In case no rules are specified 
    /// the default is to take all nodes.
    /// </summary>
    /// <param name="node"></param>
    /// <returns>true if the node match any of the selection rules, false otherwise.</returns>
    public Boolean selectNode(Opc.Ua.Export.UAVariable node){
        if(skipSelection) return true;

        string target = "";
        switch(_config.targetIdentifier.ToLower()){
            case "displayname":
                if(node.DisplayName != null && node.DisplayName[0] != null) target = node.DisplayName[0].Value;
                break;
            case "browsename":
                target = node.BrowseName; 
                break;
            case "nodeid":
                target = node.NodeId;
                break;
            default:
                logger.Debug("This should not happen, targetID = {0}",_config.targetIdentifier.ToLower());
                target = "TheseAreNotTheDroidsYouAreLookingFor";
                break;
        }
        return selectNode(target);
    }
    /// <summary>
    /// Selects the provided node against the selection rules in the node config. In case no rules are specified 
    /// the default is to take all nodes.
    /// </summary>
    /// <param name="node"></param>
    /// <returns>true if the node match any of the selection rules, false otherwise.</returns>
    public Boolean selectNode(ReferenceDescription node){
        if(skipSelection) return true;
        string target = getNameFromReference(node);
        return selectNode(target);
    }
    public string getNameFromReference(ReferenceDescription node){
        string target = "";
        switch(_config.targetIdentifier.ToLower()){
            case "displayname":
                if(node.DisplayName != null) target = node.DisplayName.Text;
                break;
            case "browsename":
                target = node.BrowseName.Name; 
                break;
            case "nodeid":
                target = node.NodeId.Identifier.ToString();
                break;
            default:
                logger.Debug("This should not happen, targetID = {0}",_config.targetIdentifier.ToLower());
                target = "TheseAreNotTheDroidsYouAreLookingFor";
                break;
        }
        return target;
    }
    
    /// <summary>
    /// Selects the provided string against the selection rules in the node config. In case no rules are specified 
    /// the default is to take all nodes.
    /// </summary>
    /// <param name="target"></param>
    /// <returns>true if the node match any of the selection rules, false otherwise.</returns>
    public Boolean selectNode(string target){

        // checks 
        if(Array.IndexOf(_config.whiteList,target) > -1) return true;
        if(Array.IndexOf(_config.blackList,target) > -1 ) return false;

        foreach (string txt in _config.notContain){
            if(target.Contains(txt)) return false;
        }

        foreach (string txt in _config.contains){
            if(target.Contains(txt)) return true;
        }

        foreach (string pattern in _config.matchRegEx){   
            Match m = Regex.Match(target, pattern, RegexOptions.IgnoreCase);
            if(m.Success) return true;
        }

        return false;
    }

}

}