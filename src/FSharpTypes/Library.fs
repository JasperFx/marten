module FSharpTypes

open System
open System.Linq.Expressions
open Marten.Testing.Documents
open Microsoft.FSharp.Linq.RuntimeHelpers

type OrderId = Id of Guid

type Order = { Id: OrderId; CustomerName: string }

type OrderIdDUWithMultipleCases =
    | IdPart1 of Guid
    | IdPart2 of Guid

type RecordTypeOrderId = { Part1: string; Part2: string }

type ArbitraryClass() =
    member this.Value = "ok"

let rec stripFSharpFunc (expression: Expression) =
    match expression with
    | :? MethodCallExpression as callExpression when callExpression.Method.Name = "ToFSharpFunc" ->
        stripFSharpFunc callExpression.Arguments.[0]
    | _ -> expression

let toLinqExpression expr  =
    expr
    |> LeafExpressionConverter.QuotationToExpression
    |> stripFSharpFunc
    |> unbox<System.Linq.Expressions.Expression<System.Func<Target, bool>>>
let greaterThanWithFsharpDateOption =
    <@ fun (o1: Target) -> o1.FSharpDateTimeOffsetOption >= Some DateTimeOffset.UtcNow  @> |> toLinqExpression
let lesserThanWithFsharpDateOption = <@ (fun (o1: Target) -> o1.FSharpDateTimeOffsetOption <= Some DateTimeOffset.UtcNow ) @> |> toLinqExpression
let greaterThanWithFsharpDecimalOption = <@ (fun (o1: Target) -> o1.FSharpDecimalOption >= Some 5m ) @> |> toLinqExpression
let lesserThanWithFsharpDecimalOption = <@ (fun (o1: Target) -> o1.FSharpDecimalOption <= Some 5m ) @> |> toLinqExpression
let greaterThanWithFsharpStringOption = <@ (fun (o1: Target) -> o1.FSharpStringOption >= Some "MyString" ) @> |> toLinqExpression
let lesserThanWithFsharpStringOption = <@ (fun (o1: Target) -> o1.FSharpStringOption <= Some "MyString" ) @> |> toLinqExpression


