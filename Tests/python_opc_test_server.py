import sys
sys.path.insert(0, "..")
import time


from opcua import ua, Server


if __name__ == "__main__":

    # setup our server
    server = Server()
    server.set_endpoint("opc.tcp://0.0.0.0:4840/freeopcua/server/")

    # setup our own namespace, not really necessary but should as spec
    uri = "http://examples.freeopcua.github.io"
    idx = server.register_namespace(uri)

    # get Objects node, this is where we should put our nodes
    objects = server.get_objects_node()

    # populating our address space
    myobj = objects.add_object(idx, "MyObject")
    myvar = myobj.add_variable(idx, "MyVariable", 6.7)
    myvar.set_writable()    # Set MyVariable to be writable by clients
    myvar2 = myobj.add_variable(idx, "MyVariable2", 6.7)
    myvar2.set_writable()    # Set MyVariable to be writable by clients
    myvar3 = myobj.add_variable(idx, "MyVariable3", 6.7)
    myvar3.set_writable()    # Set MyVariable to be writable by clients

    # starting!
    server.start()
    
    try:
        count = 1
        myvar.set_value(count)
        while True:
            time.sleep(1)
            count = myvar.get_value()
            count += 0.1
            myvar.set_value(count)
    finally:
        #close connection, remove subcsriptions, etc
        server.stop()
