module Tests

open System.IO
open Xunit
open Swensen.Unquote
open EventPersistent

module FrameworkExample =
    type User = User of string
    type Url = Url of string
    type State = { items: Map<User, Url Set> }

    type Msg =
        | UrlAdded of user: User * url: Url
        | UrlRemoved of user: User * url: Url

    module Domain =
        let reduce state =
            function
            | UrlAdded (user, url) ->
                let urls =
                    Map.tryFind user state.items
                    |> Option.defaultValue Set.empty
                    |> Set.add url

                { state with
                      items = Map.add user urls state.items }
            | UrlRemoved (user, url) ->
                let urls =
                    Map.tryFind user state.items
                    |> Option.defaultValue Set.empty
                    |> Set.remove url

                { state with
                      items = Map.add user urls state.items }

open FrameworkExample

[<Fact>]
let test () =
    use db =
        new LiteDBWrapper.Wrapper(new LiteDB.LiteDatabase(new MemoryStream()))

    async {
        let reducer =
            Persistent.create (db, "log", { items = Map.empty }, Domain.reduce)

        let! _ =
            reducer (fun _ ->
                [ UrlAdded(User "user1", Url "url1")
                  UrlAdded(User "user2", Url "url2") ])

        let! actual = reducer (fun _ -> [])

        let expected =
            { items =
                  Map.ofList [ User "user1", Set.singleton (Url "url1")
                               User "user2", Set.singleton (Url "url2") ] }

        test <@ actual = expected @>

        let! _ = reducer (fun _ -> [ UrlRemoved(User "user2", Url "url2") ])

        let! actual = reducer (fun _ -> [])

        let expected =
            { items =
                  Map.ofList [ User "user1", Set.singleton (Url "url1")
                               User "user2", Set.empty ] }

        test <@ actual = expected @>

        let reducer =
            Persistent.create (db, "log", { items = Map.empty }, Domain.reduce)

        let! actual = reducer (fun _ -> [])

        test <@ actual = expected @>
    }
    |> Async.RunSynchronously
