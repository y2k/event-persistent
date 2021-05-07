module EventPersistent.Tea

open System.Threading

type private Cmd =
    | ListenForUpdate of AsyncReplyChannel<unit>
    | Notify

type 'e t =
    private
        { mutex: SemaphoreSlim
          mailbox: MailboxProcessor<Cmd>
          updateFs: ('e list -> unit) list ref }

let init () =
    { mutex = new SemaphoreSlim(1)
      updateFs = ref []
      mailbox =
          MailboxProcessor.Start
              (fun inbox ->
                  async {
                      let pendingEvents : AsyncReplyChannel<unit> list ref = ref []

                      while true do
                          match! inbox.Receive() with
                          | ListenForUpdate r -> pendingEvents := r :: !pendingEvents
                          | Notify ->
                              for r in !pendingEvents do
                                  r.Reply()
                                  pendingEvents := []
                  }) }

let private dispatch state t f =
    async {
        do! t.mutex.WaitAsync() |> Async.AwaitTask

        let oldState = !state
        let state', es = f oldState
        state := state'

        for u in !t.updateFs do
            u es

        t.mutex.Release() |> ignore

        if not <| List.isEmpty es then
            t.mailbox.Post Notify

        return oldState
    }

let make (t: 'event t) (initState: 'state) (merge: 'state -> 'event -> 'state) =
    let state = ref initState

    let update es =
        for e in es do
            state := merge !state e

    t.updateFs := update :: !t.updateFs

    dispatch state t

let waitForChanges (t: 'event t) =
    t.mailbox.PostAndAsyncReply ListenForUpdate
