module EventPersistent.Store

type private 'event Cmd =
    | RegisterUpdate of ('event list -> unit)
    | Update of (unit -> 'event list) * AsyncReplyChannel<unit>

type 'e t =
    private
        { mailbox: MailboxProcessor<'e Cmd> }

let init () =
    { mailbox =
          MailboxProcessor.Start
              (fun inbox ->
                  async {
                      let updates : _ list ref = ref []

                      while true do
                          match! inbox.Receive() with
                          | Update (update, reply) ->
                              let newEvents = update ()

                              for u in !updates do
                                  u newEvents

                              reply.Reply()
                          | RegisterUpdate update -> updates := update :: !updates
                  }) }

let make (t: 'event t) (initState: 'state) (merge: 'state -> 'event -> 'state) =
    let state = ref initState

    let afterUpdate events =
        for e in events do
            state := merge !state e

    t.mailbox.Post <| RegisterUpdate afterUpdate

    fun f ->
        async {
            let outResult : 'state option ref = ref None

            let update () =
                let oldState = !state
                let newState, newEvents = f oldState
                outResult := Some oldState
                state := newState
                newEvents

            do! t.mailbox.PostAndAsyncReply(fun r -> Update(update, r))
            return !outResult |> Option.get
        }
