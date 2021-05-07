module TeaTests

open Xunit
open Swensen.Unquote

module Tea = EventPersistent.Tea

type Event =
    | Added1 of int
    | Added2 of int

[<Fact>]
let test () =
    let random = System.Random()

    let work1 dispatch =
        async {
            for i in [ 1 .. 5 ] do
                do! Async.Sleep(random.Next 100)
                do! dispatch (fun (a, b) -> (a + i, b), [ Added1 i ])
        }

    let work2 dispatch =
        async {
            for i in [ 1 .. 5 ] do
                do! Async.Sleep(random.Next 100)
                do! dispatch (fun (a, b) -> (a + i, b), [ Added2 i ])
        }

    async {
        let store : Event Tea.t = Tea.init ()

        let dispatch1 =
            Tea.make
                store
                (0, 0)
                (fun (a, b) e ->
                    match e with
                    | Added2 i -> (a, b + i)
                    | _ -> (a, b))

        let dispatch2 =
            Tea.make
                store
                (0, 0)
                (fun (a, b) e ->
                    match e with
                    | Added1 i -> (a, b + i)
                    | _ -> (a, b))

        do!
            [ work1 dispatch1; work2 dispatch2 ]
            |> Async.Parallel
            |> Async.Ignore

        let actual = ref (0, 0)

        do!
            dispatch1
                (fun state ->
                    actual := state
                    state, [])

        test <@ !actual = (15, 15) @>

        let actual = ref (0, 0)

        do!
            dispatch2
                (fun state ->
                    actual := state
                    state, [])

        test <@ !actual = (15, 15) @>
    }
    |> Async.RunSynchronously
