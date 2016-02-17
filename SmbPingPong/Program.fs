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

// mono  --debug ./SmbPingPong.exe --directory $(pwd) --pong-mode tcp://somehost:5556
let pong connectToSub connectToPub directory =
  use context = new Context()
  use subscriber = sub context
  Socket.subscribe subscriber [""B]
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

// mono  --debug ./SmbPingPong.exe --directory $(pwd) --ping-mode tcp://127.0.0.1:5556
let ping connectSub connectPub dir =
  use context = new Context()
  use subscriber = sub context
  Socket.subscribe subscriber [""B]
  printfn "ping: connecting subscriber to %s" connectSub
  connectSub |> connect subscriber
  
  use publisher = pub context
  printfn "ping: connecting publisher to %s" connectPub
  connectPub |> connect publisher

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
      printfn "(%i) got NACK - REPRO, yey!" i
      loop (i + 1u)

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
  use context = new Context()
  use toBackendSub = Context.xsub context
  printfn "proxy: to-backend subscriber to %s" "tcp://*:5555"
  bind toBackendSub "tcp://*:5555"

  use toFrontendSub = Context.xsub context
  printfn "proxy: to-frontend subscriber to %s" "tcp://*:6555"
  bind toFrontendSub "tcp://*:6555"

  use toBackendPub = Context.xpub context
  printfn "proxy: to-backend publisher to %s" "tcp://*:5556"
  bind toBackendPub "tcp://*:5556"

  use toFrontendPub = Context.xpub context
  printfn "proxy: to-frontend publisher to %s" "tcp://*:6556"
  bind toFrontendPub "tcp://*:6556"

  spawn <| fun _ ->
    printfn "%s" "spawning to-backend proxy"
    fszmq.Proxying.proxy toBackendSub toBackendPub None

  printfn "%s" "spawning to-frontend proxy"
  fszmq.Proxying.proxy toFrontendSub toFrontendPub None

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
  | PingMode (connectSub, connectPub) ->
    let dir = parsed.GetResult <@ Directory @>
    ping connectSub connectPub dir

  | PongMode (connectSub, connectPub) ->
    let dir = parsed.GetResult <@ Directory @>
    pong connectSub connectPub dir

  | ProxyMode _ ->
    proxy ()

  | _ ->
    eprintfn "%s" (parser.Usage())

  0