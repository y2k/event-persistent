namespace LiteDBWrapper

open System
open LiteDB
open EventPersistent.Persistent

type Collection<'a>(col: ILiteCollection<'a>) =
    interface ICollection<'a> with
        member _.Insert x = col.Insert x |> ignore

        member _.FindGreatThen(id, value) =
            col.Find(Query.GT(id, BsonValue.op_Implicit value))

type Wrapper(db: ILiteDatabase) =
    interface IDisposable with
        member _.Dispose() = db.Dispose()

    interface IDatabse with
        member _.BeginTrans() = db.BeginTrans() |> ignore
        member _.Commit() = db.Commit() |> ignore

        member _.GetCollection name =
            Collection(db.GetCollection<'a>(name)) :> ICollection<'a>
