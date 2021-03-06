﻿namespace Player

open Akka.FSharp
open Cards
open Click
open Import
open Preflop
open System.Drawing
open Hands
open Cards.HandValues
open Actions
open Recognition.ScreenRecognition
open PostFlop.HandValue
open PostFlop.Decision
open PostFlop.Facade

module Decide =
  open Interaction

  let fileNameIP = System.IO.Directory.GetCurrentDirectory() + @"\IPinput.xlsx"
  let rulesIP = importRuleFromExcel importRulesIP fileNameIP |> List.ofSeq
  let fileNameOOP = System.IO.Directory.GetCurrentDirectory() + @"\OOPinput.xlsx"
  let rulesOOP = importRuleFromExcel importRulesOOP fileNameOOP |> List.ofSeq
  let rules = Seq.concat [|rulesIP;rulesOOP|]
  let decidePre stack = decideOnRules rules stack

  let understandHistory (screen: Screen) =
    let raise bet bb = 
      let b = (bet |> decimal) / (bb |> decimal)
      Raise(b, b)
    let villainAllIn = screen.VillainStack = Some 0
    match screen.Button, screen.Blinds, screen.VillainBet, screen.HeroBet with
    | Hero, Some {BB = bb; SB = sb}, Some vb, Some hb when vb <= bb && sb = hb -> []
    | Hero, Some {BB = bb}, Some vb, Some hb when vb > bb && bb = hb -> [Limp; raise vb bb]
    | Hero, Some {BB = bb}, Some vb, Some hb when hb > bb && vb > hb && villainAllIn -> [raise hb bb; RaiseAllIn]
    | Hero, Some {BB = bb}, Some vb, Some hb when hb > bb && vb > hb -> [raise hb bb; raise vb bb]
    | Villain, Some {BB = bb}, Some vb, Some hb when vb = bb && hb = bb -> [Limp]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb = bb && vb > bb && villainAllIn -> [RaiseAllIn]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb = bb && vb > bb -> [raise vb bb]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb > bb && hb < 4 * bb && vb > hb && villainAllIn -> [Limp; raise hb bb; RaiseAllIn]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb > bb && hb < 4 * bb && vb > hb -> [Limp; raise hb bb; raise vb bb]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb > bb && vb > hb && villainAllIn -> [raise ((hb + bb) / 2) bb; raise hb bb; RaiseAllIn]
    | Villain, Some {BB = bb}, Some vb, Some hb when hb > bb && vb > hb -> [raise ((hb + bb) / 2) bb; raise hb bb; raise vb bb]
    | _ -> failwith "History is not clear"

  let decide' xlFlopTurn xlTurnDonkRiver (screen: Screen): Action option =
    let decidePre (screen: Screen): Action option =
      match screen.IsVillainSitout, screen.HeroStack, screen.HeroBet, screen.VillainStack, screen.VillainBet, screen.Blinds with
      | true, _, _, _, _, _ -> Some Action.MinRaise
      | _, Some hs, Some hb, Some vs, Some vb, Some b -> 
        let stack = min (hs + hb) (vs + vb)
        let effectiveStack = decimal stack / decimal b.BB
        let fullHand = parseFullHand screen.HeroHand
        let history = understandHistory screen
        let actionPattern = decidePre effectiveStack history fullHand
        Option.map (mapPatternToAction vb stack) actionPattern  
      | _ -> None
    let decidePost (screen: Screen) =
      match screen.IsVillainSitout, screen.TotalPot, screen.HeroStack, screen.VillainStack, screen.Blinds with
      | true, _, _, _, _ -> Some Action.MinRaise
      | _, Some tp, Some hs, Some vs, Some b -> 
        let suitedHand = screen.HeroHand |> parseSuitedHand
        let board = screen.Board |> parseBoard
        let value = handValueWithDraws suitedHand board
        let special = boardTexture board
        let vb = defaultArg screen.VillainBet 0
        let hb = defaultArg screen.HeroBet 0
        let s = { Hand = suitedHand; Board = board; Pot = tp; VillainStack = vs; HeroStack = hs; VillainBet = vb; HeroBet = hb; BB = b.BB }
        decidePostFlop s value special xlFlopTurn xlTurnDonkRiver
      | _ -> None

    match screen.Board with
    | null -> decidePre screen
    | _ -> decidePost screen

  type DecisionMessage = {
    WindowTitle: string
    TableName: string
    Screen: Screen
    Bitmap: Bitmap
  }

  let mapAction action buttons : ClickAction[] =
    let findButton names =
      names 
      |> List.choose (fun x -> Seq.tryFind (fun y -> x = y.Name) buttons)
      |> List.tryHead
    let button =
      match action with
      | Action.Fold -> ["Check"; "Fold"]
      | Action.Check -> ["Check"]
      | Action.Call -> ["Call"; "AllIn"]
      | Action.MinRaise -> ["RaiseTo"; "Bet"; "Call"]
      | Action.RaiseToAmount _ -> ["RaiseTo"; "Bet"]
      | Action.AllIn -> ["AllIn"; "RaiseTo"; "Bet"; "Call"]
      |> findButton

    match (action, button) with
    | (Action.AllIn, Some b) -> [|Click(368, 389, 42, 7); Click(b.Region)|]
    | (Action.RaiseToAmount x, Some b) -> [| Click(599, 407, 18, 9); Amount(x); Click(b.Region)|]
    | (_, Some b) -> [|Click(b.Region)|]
    | (_, None) -> failwith "Could not find an appropriate button"

  let decisionActor xlFlopTurn xlTurnDonk msg lastScreen =
    let screen = msg.Screen
    match lastScreen with
    | Some s when s = screen -> (None, lastScreen)
    | _ ->
      print screen |> Seq.iter (printfn "%s: %s" "Hand")
      let decision = decide' xlFlopTurn xlTurnDonk screen
      match decision with
      | Some d ->
        printfn "Decision is: %A" d
        let action = mapAction d screen.Actions
        printfn "Action is: %A" action
        let outMsg = { WindowTitle = msg.WindowTitle; Clicks = action; IsInstant = screen.IsVillainSitout }
        (Some outMsg, Some screen)
      | None ->
        printfn "Could not make a decision, dumping the screenshot..."
        Dumper.SaveBitmap(msg.Bitmap, msg.TableName)
        (None, Some screen)