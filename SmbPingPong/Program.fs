module Program

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
let inline decode value = Encoding.UTF8.GetString value
let inline spawn fn = Thread(ThreadStart fn).Start()

// mono  --debug ./SmbPingPong.exe --directory $(pwd) --pong-mode
let pong directory =
  use context = new Context()
  use server  = rep context
  bind server "tcp://*:5556"

  let rec loop () =
    match server |> recv |> decode with
    | fileName when fileName.EndsWith ".txt" ->
      let path = Path.Combine (directory, fileName)
      let contents = File.ReadAllText path

      if contents.Length = 0 then
        eprintfn "File contents are empty at path %s" path
        "NACK"B |>> server
        loop ()
      else
        printfn "File contents length: %i" contents.Length
        "ACK"B |>> server
        loop ()

    | _ ->
      "goodbye"B |>> server

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
let ping connectTo dir =
  use context = new Context()
  use client  = req context
  connectTo |> connect client

  let rec loop i =
    let fileName = createFileName ()
    let filePath = Path.Combine (dir, fileName)
    let contents = createFile filePath
    fileName |> encode |> send client
    printfn "(%i) sent: %s with contents %s" i filePath contents

    match recv client with
    | null ->
      ()

    | bs ->
      let reply = decode bs
      printfn "(%i) got: %s" i reply
      loop (i + 1u)

  loop 1u

type Args =
  | [<Mandatory>] Directory of string
  | [<PrintLabels>] Ping_Mode of other:string
  | Pong_Mode
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Directory _ -> "Where to write files to in Ping mode"
      | Ping_Mode _ -> "Runs Rutta in Shipper/PUB mode (send Messages from a node to proxy)"
      | Pong_Mode -> "Server/receiver that tries to read the file from disk"

[<EntryPoint>]
let main argv =
  let parser = ArgumentParser.Create<Args>()
  let parsed = parser.Parse argv
  let dir = parsed.GetResult <@ Directory @>
  let pingMode = parsed.TryGetResult <@ Ping_Mode @>

  printfn "libzmq version: %A" ZMQ.version

  match pingMode with
  | Some pongBinding ->
    ping pongBinding dir

  | _ ->
    pong dir

  0