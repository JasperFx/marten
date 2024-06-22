module FSharpTypes

open System

type OrderId = Id of Guid

type Order = { Id: OrderId; CustomerName: string }

type OrderIdDUWithMultipleCases =
    | IdPart1 of Guid
    | IdPart2 of Guid

type RecordTypeOrderId = { Part1: string; Part2: string }

type ArbitraryClass() =
    member this.Value = "ok"
