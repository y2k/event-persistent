module EventPersistent.Persistent

open LiteDB
open System.Text.Json
open System.Text.Json.Serialization

let private options = JsonSerializerOptions()
options.Converters.Add(JsonFSharpConverter())

[<CLIMutable>]
type Wrapper = { id: int; value: string }

let private serialize x = JsonSerializer.Serialize(x, options)
let private deserialize (s: string) = JsonSerializer.Deserialize(s, options)

let create (db: LiteDatabase, name: string, init: 's, f: 's -> 'msg -> 's) =
    let col = db.GetCollection<Wrapper>(name)
    let index = ref 0
    let state = ref init

    fun (fxs: _ -> 'msg list) ->
        async {
            db.BeginTrans() |> ignore

            let prevs =
                col.Find(Query.GT("_id", BsonValue.op_Implicit !index))

            for aw in prevs do
                let a = aw.value
                let x = deserialize a
                index := aw.id
                state := f !state x

            let result = !state

            let xs = fxs !state

            for x in xs do
                state := f !state x
                let a = serialize x
                let aw = { id = 0; value = a }
                col.Insert aw |> ignore
                index := aw.id

            db.Commit() |> ignore

            return result
        }
