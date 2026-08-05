// Standalone C# verifier: proves a Python-generated pack verifies in C#.
// Usage: dotnet run --project tools/PackVerifier -- <path-to-pack.json>
open System
open System.IO

let packPath = fsi.CommandLineArgs.[1]
let json = File.ReadAllText(packPath)
let verified = Runewake.Engine.Cards.ContentManager.VerifyPackJson(json)
printfn "VERIFY_RESULT=%b" verified
if not verified then
    failwith "Pack failed C# verification"