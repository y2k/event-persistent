module EventPersistent.Tea

type 'event UpdateHolder =
    abstract member Dispatch : ('state -> 'state * 'event list) -> unit

type private 'event Cmd =
    | RegisterUpdate of ('event list -> unit)
    | Update of (unit -> 'event list) * AsyncReplyChannel<unit>
    | ListenForUpdate of AsyncReplyChannel<unit>

type 'e t =
    private
        { mailbox : MailboxProcessor<'e Cmd> }

let init () =
    { mailbox =
          MailboxProcessor.Start
              (fun inbox ->
                  async {
                      let pendingEvents : AsyncReplyChannel<unit> list ref = ref []
                      let updates : _ list ref = ref []

                      while true do
                          match! inbox.Receive() with
                          | Update (update, reply) ->
                              let newEvents = update ()

                              for u in !updates do
                                  u newEvents

                              reply.Reply()

                              for r in !pendingEvents do
                                  r.Reply()
                                  pendingEvents := []
                          | RegisterUpdate update -> updates := update :: !updates
                          | ListenForUpdate r -> pendingEvents := r :: !pendingEvents
                  }) }

let make (t : 'event t) (initState : 'state) (merge : 'state -> 'event -> 'state) =
    let state = ref initState

    let afterUpdate events =
        for e in events do
            state := merge !state e

    t.mailbox.Post <| RegisterUpdate afterUpdate

    fun (f : 'state -> 'state * 'event list) ->
        let update () =
            let newState, newEvents = f !state
            state := newState
            newEvents

        t.mailbox.PostAndAsyncReply(fun r -> Update(update, r))

let waitForChanges (t : 'event t) =
    t.mailbox.PostAndTryAsyncReply(ListenForUpdate, 60_000)
    |> Async.Ignore
