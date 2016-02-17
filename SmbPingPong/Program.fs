module SmbPingPong.Program

open System
open System.Text
open System.IO
open System.Globalization
open System.Threading
open System.Security.Cryptography
open Argu
open fszmq
open fszmq.Context
open fszmq.Socket
  
let inline encode value = value |> string |> Encoding.UTF8.GetBytes
let inline decode value =
  match value with
  | null -> ""
  | _ -> Encoding.UTF8.GetString value
let inline spawn fn = Thread(ThreadStart fn).Start()

let pong connectToSub connectToPub directory =
  use context = new Context()
  use subscriber = sub context
  subscribe subscriber [""B]
  printfn "pong: connecting subscriber to %s" connectToSub
  connect subscriber connectToSub

  use publisher = pub context
  printfn "pong: connecting publisher to %s" connectToPub
  connect publisher connectToPub
  
  let rec loop () =
    match subscriber |> recv |> decode with
    | fileName when fileName.EndsWith ".txt" ->
      let path = Path.Combine (directory, fileName)
      let contents = File.ReadAllText path

      if contents.Length = 0 then
        eprintfn "File contents are empty at path %s" path
        "NACK"B |>> publisher

      else
        printfn "File contents length: %i" contents.Length
        "ACK"B |>> publisher
        loop ()

    | _ ->
      printfn "%s" "exiting"
      "BYE"B |>> publisher

  loop ()

let createFileName () =
  Guid.NewGuid().ToString().Replace("-", "").ToLowerInvariant() + ".txt"

let rng = RandomNumberGenerator.Create()

let createFile filePath =
  let rndData = Array.zeroCreate 8
  do rng.GetBytes rndData
  let rndNo = BitConverter.ToInt64(rndData, 0)
  let contents = rndNo.ToString(CultureInfo.InvariantCulture)
  use fs = File.OpenWrite(filePath)
  use sw = new StreamWriter(fs)
  sw.WriteLine contents
  contents

let ping read write dir =
  use context = new Context()
  use subscriber = sub context
  Socket.subscribe subscriber [""B]
  printfn "ping: connecting subscriber to %s" read
  read |> connect subscriber

  use publisher = pub context
  printfn "ping: connecting publisher to %s" write
  write |> connect publisher

  let rec loop i =
    let fileName = createFileName ()
    let filePath = Path.Combine (dir, fileName)
    let contents = createFile filePath
    fileName |> encode |> send publisher
    printfn "(%i) sent: %s with contents %s" i filePath contents

    match (recv >> decode) subscriber with
    | null ->
      ()

    | "ACK" ->
      printfn "(%i) got ACK" i
      loop (i + 1u)

    | "NACK" ->
      printfn "(%i) got NACK - REPRO!!!" i

    | "BYE"
    | _ ->
      printfn "Server says bye, exiting..."

  loop 1u

/// When the frontend is a ZMQ_XSUB socket, and the backend is a ZMQ_XPUB socket, the proxy
/// shall act as a message forwarder that collects messages from a set of publishers and
/// forwards these to a set of subscribers. This may be used to bridge networks transports,
/// e.g. read on tcp:// and forward on pgm://.
///
/// ```
///  ping   | connect ----> bind toBackendSub tcp://XX:5555  |  proxy  | bind toBackendPub tcp://XX:5556  <---- connect |  pong
///         | connect ----> bind toFrontendPub tcp://XX:6556 |         | bind toFrontendSub tcp://XX:6555 <---- connect |
/// ```
let proxy () =
  printfn """use proxy:
  ping-mode takes the read-socket and then the write-socket
  (frontend) --ping-mode tcp://127.0.0.1:6556 tcp://127.0.0.1:5555

  ping-mode takes the read-socket and then the write-socket
  (backend) --pong-mode tcp://127.0.0.1:5556 tcp://127.0.0.1:6555
"""

  use context = new Context()
  use frontWrite = Context.xsub context
  bind frontWrite "tcp://*:5555"

  use backWrite = Context.xsub context
  bind backWrite "tcp://*:6555"

  use backRead = Context.xpub context
  bind backRead "tcp://*:5556"

  use frontRead = Context.xpub context
  bind frontRead "tcp://*:6556"

  spawn <| fun _ -> 
    printfn "%s" "spawning to-backend proxy"
    fszmq.Proxying.proxy frontWrite backRead None

  printfn "%s" "spawning to-frontend proxy"
  fszmq.Proxying.proxy backWrite frontRead None

type Args =
  | Directory of string
  | [<PrintLabels>] Ping_Mode of connectSub:string * connectPub:string
  | [<PrintLabels>] Pong_Mode of connectSub:string * connectPub:string
  | Proxy_Mode
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Directory _ -> "Where to write files to in Ping mode"
      | Ping_Mode _ -> "Client/sender that writes files and then does req-repl to the server"
      | Pong_Mode _ -> "Server/receiver that tries to read the file from disk"
      | Proxy_Mode -> "Forwards from client/ping to server/pong. Client send to :5555, read :6556. Server read :5556, send to :6556."
      
let (|PingMode|_|) (args : ParseResults<Args>) : (string * string) option =
  args.TryGetResult <@ Ping_Mode @>

let (|PongMode|_|) (args : ParseResults<Args>) : (string * string) option =
  args.TryGetResult <@ Pong_Mode @>

let (|ProxyMode|_|) (args : ParseResults<Args>) : Args option =
  args.TryGetResult <@ Proxy_Mode @>

[<EntryPoint>]
let main argv =
  let parser = ArgumentParser.Create<Args>()
  let parsed = parser.Parse argv
  printfn "libzmq version: %A, running ping.exe version %s" ZMQ.version (App.getVersion ())

  match parsed with
  | PingMode (read, write) ->
    let dir = parsed.GetResult <@ Directory @>
    ping read write dir

  | PongMode (read, write) ->
    let dir = parsed.GetResult <@ Directory @>
    pong read write dir

  | ProxyMode _ ->
    proxy ()

  | _ ->
    eprintfn "%s" (parser.Usage())

  0