# SmbPingPong

Small utility to test whether SMB is using session semantics on writes in distributed systems.

You spawn one 'ping' and one 'pong' node with a specified directory. Ping will now write a file to this folder and wait for pong to read it. Pong will get the message that a file is present, try to read it, print its length and then if it's empty, send NACK back to ping or if it has contents, send ACK back to ping.

By spawning these processes on two different nodes you can see if it's possible to beat visibility of the same files between two nodes.

This work was prompted by SMB seemingly ACKing writes on one node, but presenting an empty file when that file is read from another node.

Future work: to add a proxy so that these ping/pong modes can be run in different subnets.
