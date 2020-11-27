module EventPersistent.Tea

open System.Threading

type private Cmd =
    | Cmd of AsyncReplyChannel<unit>
    | Notify

type 'e t =
    private
        { mutex: SemaphoreSlim
          mailbox: MailboxProcessor<Cmd>
          xs: ('e list -> unit) list ref }

let init () =
    { mutex = new SemaphoreSlim(1)
      xs = ref []
      mailbox =
          MailboxProcessor.Start
              (fun inbox ->
                  async {
                      let pendingEvents: AsyncReplyChannel<unit> list ref = ref []

                      while true do
                          match! inbox.Receive() with
                          | Cmd r -> pendingEvents := r :: !pendingEvents
                          | Notify ->
                              for r in !pendingEvents do
                                  r.Reply()
                                  pendingEvents := []
                  }) }

let make (t: 'event t) (initState: 'state) (merge: 'state -> 'event -> 'state) =
    let state = ref initState

    let update es =
        for e in es do
            state := merge !state e

    t.xs := update :: !t.xs

    fun f ->
        async {
            do! t.mutex.WaitAsync() |> Async.AwaitTask

            let oldState = !state
            let (state', es) = f oldState
            state := state'

            for u in !t.xs do
                u es

            t.mutex.Release() |> ignore
            return oldState
        }

let make2 (t: 'event t) (initState: 'state) (merge: 'state -> 'event -> 'state) =
    let state = ref initState

    let update es =
        for e in es do
            state := merge !state e

    t.xs := update :: !t.xs

    let updateState f =
        async {
            do! t.mutex.WaitAsync() |> Async.AwaitTask

            let oldState = !state
            let (state', es) = f oldState
            state := state'

            for u in !t.xs do
                u es

            t.mutex.Release() |> ignore
            return oldState
        }

    let rec waitForChanged f =
        async {
            let invalidated = ref false
            do! t.mutex.WaitAsync() |> Async.AwaitTask
            invalidated := f !state
            t.mutex.Release() |> ignore

            if not !invalidated then
                do! t.mailbox.PostAndAsyncReply Cmd
                do! waitForChanged f
        }

    updateState, waitForChanged
