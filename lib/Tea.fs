module EventPersistent.Tea

open System.Threading

type private 'event Cmd =
    | RegisterUpdate of ('event list -> unit)
    | ListenForUpdate of AsyncReplyChannel<unit>
    | Notify

type 'e t =
    private
        { mutex: SemaphoreSlim
          mailbox: MailboxProcessor<'e Cmd>
          updateFs: ('e list -> unit) list ref }

let init () =
    { mutex = new SemaphoreSlim(1)
      updateFs = ref []
      mailbox =
          MailboxProcessor.Start
              (fun inbox ->
                  async {
                      let pendingEvents : AsyncReplyChannel<unit> list ref = ref []
                      let updates : _ list ref = ref []

                      while true do
                          match! inbox.Receive() with
                          | RegisterUpdate update -> updates := update :: !updates
                          | ListenForUpdate r -> pendingEvents := r :: !pendingEvents
                          | Notify ->
                              for r in !pendingEvents do
                                  r.Reply()
                                  pendingEvents := []
                  }) }

let private dispatch state t (f : 'state -> 'state * 'event list) =
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
    let update events =
        for e in events do
            state := merge !state e

    t.mailbox.Post <| RegisterUpdate update
    
    t.updateFs := update :: !t.updateFs

    dispatch state t

let waitForChanges (t: 'event t) =
    t.mailbox.PostAndAsyncReply ListenForUpdate
