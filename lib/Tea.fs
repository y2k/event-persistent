module EventPersistent.Tea

type IReducer<'state, 'event> =
    abstract member Invoke : ('state -> 'state * 'event list * 'result) -> 'result Async

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

let make (t : 'event t) (initState : 'state) (merge : 'state -> 'event -> 'state) : IReducer<'state, 'event> =
    let state = ref initState

    let afterUpdate events =
        for e in events do
            state := merge !state e

    t.mailbox.Post <| RegisterUpdate afterUpdate

    { new IReducer<'state, 'event> with
        member _.Invoke(f : _ -> _ * _ * 'r) =
            async {
                let outResult : 'r option ref = ref None

                let update () =
                    let newState, newEvents, result = f !state
                    outResult := Some result
                    state := newState
                    newEvents

                do! t.mailbox.PostAndAsyncReply(fun r -> Update(update, r))
                return !outResult |> Option.get
            } }

let waitForChanges (t : 'event t) =
    t.mailbox.PostAndTryAsyncReply(ListenForUpdate, 60_000)
    |> Async.Ignore
