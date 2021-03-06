﻿namespace PostFlop

open Cards
open Hands
open Options

module Decision =

  type Street = Flop | Turn | River

  type Snapshot = {
    Pot: int 
    VillainStack: int 
    HeroStack: int 
    VillainBet: int 
    HeroBet: int
    BB: int
    Hand: SuitedHand
    Board: Board
  }

  let street s = 
    match s.Board.Length with
    | 5 -> River
    | 4 -> Turn 
    | 3 -> Flop
    | _ -> failwith "Weird board length"

  let roundTo5 v = (v + 2) / 5 * 5
  let potPre s = s.Pot - s.HeroBet - s.VillainBet
  let betPre s = (potPre s) / 2
  let stackPre s = betPre s + min (s.HeroStack + s.HeroBet) (s.VillainStack + s.VillainBet)
  let stack s = min s.HeroStack s.VillainStack
  let effectiveStackOnCurrentStreet s = min (s.HeroStack + s.HeroBet) (s.VillainStack + s.VillainBet)
  let effectiveStackPre s = (stackPre s + s.BB / 2 - 1) / s.BB
  let callSize s = min (s.VillainBet - s.HeroBet) s.HeroStack
  let stackIfCall s = min (s.HeroStack - (callSize s)) s.VillainStack
  let potOdds s = (callSize s |> decimal) * 100m / (s.Pot + (callSize s) |> decimal) |> ceil |> int
  let times i d = ((i |> decimal) * d) |> int

  let cbet pot cbetf = (pot |> decimal) * cbetf / 100m |> int

  let cbetOr s f defaultAction =
    let size = cbet s.Pot f.Factor
    if times size f.IfStackFactorLessThan < stack s
      && effectiveStackPre s > f.IfPreStackLessThan 
    then size |> RaiseToAmount
    else defaultAction

  let reraise s = 
    let size = s.VillainBet * 9 / 4 |> roundTo5 
    if size > (stackPre s) * 53 / 100 then Action.AllIn
    else size |> RaiseToAmount

  let callraise s =
    if (stackIfCall s) * 5 < (s.Pot + (callSize s)) 
    then Action.AllIn
    else Action.Call

  let callEQ s threshold = 
    if potOdds s <= threshold then Action.Call else Action.Fold

  let stackOffDonkX x s = 
    let raiseSize = s.VillainBet * x / 100
    if raiseSize + 100 > effectiveStackOnCurrentStreet s then Action.AllIn
    else Action.RaiseToAmount raiseSize

  let callRaiseRiver s =
    if s.VillainBet * 2 < (s.Pot - s.VillainBet) then stackOffDonkX 250 s
    else Action.Call

  let stackOffDonk s =
    if s.VillainBet = s.BB then 
      4 * s.VillainBet |> RaiseToAmount
    else
      let calculatedRaiseSize = ((stackPre s) - 85 * (potPre s) / 100) * 10 / 27
      let raiseSize =
        if calculatedRaiseSize > s.VillainBet * 2 then calculatedRaiseSize
        else (9 * s.VillainBet / 4)
        |> roundTo5 
      if raiseSize < s.HeroStack then RaiseToAmount raiseSize
      else Action.AllIn

  let raisePetDonk s =
    if s.VillainBet = s.BB then 
      4 * s.VillainBet |> RaiseToAmount
    else 
      if s.VillainBet < betPre s then
        3 * s.VillainBet |> roundTo5 |> RaiseToAmount
      else if s.VillainBet * 2 > stackPre s && s.VillainStack > 0 then Action.AllIn
      else Action.Call

  let ensureMinRaise s a =
    match a with
    | Some (RaiseToAmount x) when x <= s.BB -> Some MinRaise
    | x -> x

  let decide snapshot options =
    if snapshot.VillainBet > 0 && snapshot.HeroBet = 0 then
      match options.Donk, street snapshot with
      | ForValueStackOffX(x), _ -> stackOffDonkX x snapshot |> Some
      | ForValueStackOff, _ -> stackOffDonk snapshot |> Some
      | CallRaisePet, River -> callRaiseRiver snapshot |> Some
      | CallRaisePet, _ -> raisePetDonk snapshot |> Some
      | CallEQ eq, _ -> 
        let modifiedEq = if snapshot.VillainStack = 0 && eq >= 26 then eq + 15 else eq
        callEQ snapshot modifiedEq |> Some
      | Call, _ -> Some Action.Call
      | Fold, _ -> Some Action.Fold
      | Undefined, _ -> None
    else if snapshot.VillainBet > 0 && snapshot.HeroBet > 0 then
      match options.CheckRaise with
      | OnCheckRaise.StackOff -> reraise snapshot |> Some
      | OnCheckRaise.CallEQ eq -> callEQ snapshot eq |> Some
      | OnCheckRaise.Call -> callraise snapshot |> Some
      | OnCheckRaise.AllIn -> Some Action.AllIn
      | OnCheckRaise.Fold -> Some Action.Fold
      | OnCheckRaise.Undefined -> None
    else 
      match options.CbetFactor with
      | Always f -> cbet snapshot.Pot f |> RaiseToAmount |> Some
      | OrAllIn f -> cbetOr snapshot f Action.AllIn |> Some
      | OrCheck f -> cbetOr snapshot f Check |> Some
      | Never -> Check |> Some
      | CBet.Undefined -> None
      |> ensureMinRaise snapshot