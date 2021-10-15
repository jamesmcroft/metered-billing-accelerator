module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open NodaTime
open Metering
open Metering.Types

[<SetUp>]
let Setup () = ()

let d (s: string) : LocalDate =
    LocalDate.FromDateTime(DateTime.ParseExact(s, "yyyy-MM-dd", null))

let bp (s: string) : BillingPeriod =
    s.Split([|'|'|], 3)
    |> Array.toList
    |> List.map (fun s -> s.Trim())
    |> function
        | [idx; start; ending] -> 
            { 
                FirstDay = (d start)
                LastDay = (d ending)
                Index = (uint (Int64.Parse(idx)))
            }
        | _ -> failwith "parsing error"

[<Test>]
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create "planId" Monthly (d "2021-05-13") 

    Assert.AreEqual(
        (bp "2|2021-07-13|2021-08-12"),
        (BillingPeriod.createFromIndex sub 2u))

[<Test>]
let Test_Subscription_determineBillingPeriod () =
    let vectors = [
        //           start              // interval          a date in the interval
        (Monthly, "2021-05-13", "0|2021-05-13|2021-06-12", "2021-05-30")
        (Monthly, "2021-05-13", "2|2021-07-13|2021-08-12", "2021-08-01")

        // if I purchase on the 29th of Feb in a leap year, 
        // my billing renews on 28th of Feb the next year, 
        // therefore last day of the current billing period is 27th next year
        (Annually,  "2004-02-29", "0|2004-02-29|2005-02-27", "2004-03-29") 
        (Annually,  "2021-05-13", "0|2021-05-13|2022-05-12", "2021-08-01")
        (Annually,  "2021-05-13", "1|2022-05-13|2023-05-12", "2022-08-01")
    ]

    for (interval, startStr, billingPeriodStr, inputStr) in vectors do
        let startDate = d startStr
        let dateToCheck = d inputStr
        let sub = Subscription.create "planId" interval startDate 
        let expected : Result<BillingPeriod, BusinessError> = Ok(bp billingPeriodStr)
        let compute = BillingPeriod.determineBillingPeriod sub dateToCheck
        Assert.AreEqual(expected, compute);

[<Test>]
let Test_BillingPeriod_isInBillingPeriod () =
    let sub = Subscription.create "planId" Monthly (d "2021-05-13") 
    let bp = BillingPeriod.createFromIndex sub 
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-08-13"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-08-15"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-09-12"))
    Assert.IsFalse(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-09-13"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 4u) (d "2021-09-13"))

[<Test>]
let Test_BillingPeriod_getBillingPeriodDelta () =
    let check sub expected previous current =        
        let result = (BillingPeriod.getBillingPeriodDelta sub (d previous) (d current))
        
        Assert.IsTrue((BillingPeriod.Period(expected) = result))

    let sub =  Subscription.create "planId" Monthly ("2021-05-13" |> d)
    check sub 0u "2021-05-13" "2021-05-13" // on start day
    check sub 0u "2021-08-13" "2021-08-15" // same period
    check sub 0u "2021-08-17" "2021-09-12" // same period
    check sub 1u "2021-08-17" "2021-09-13" // next period
    check sub 2u "2021-08-17" "2021-10-13" // 2 periods down the road


type MeterValue_deductVector = { State: MeterValue; Quantity: Quantity; Expected: MeterValue}
[<Test>]
let Test_MeterValue_deduct() =
    [ 
        {
            // if Monthly is sufficient, don't touch annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL }
            Quantity = 8UL
            Expected = IncludedQuantity { 
                Annually = Some 30UL
                Monthly = Some 2UL }
        }
        {
            // if Monthly is not sufficient, also deduct from annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL}
            Quantity = 13UL
            Expected = IncludedQuantity { 
                Annually = Some 27UL
                Monthly = None}
        }
        {
            // if both Monthly and Annual are not sufficient, it costs money
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL }
            Quantity = 43UL
            Expected = ConsumedQuantity { Amount = 3UL }
        }
        {
            // If there's nothing, it costs money
            State = IncludedQuantity { Annually = None; Monthly = None}
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 0UL }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 10UL }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 12UL }
        }
    ] 
    |> List.map(fun { State=state; Quantity=quantity; Expected=expected} -> Assert.AreEqual(expected, MeterValue.deduct state quantity))
    |> ignore

type MeterValue_topupMonthlyCredits_Vector = { Input: MeterValue; Values: (Quantity * RenewalInterval) list; Expected: MeterValue}

[<Test>]
let Test_MeterValue_topupMonthlyCredits() =    
    [
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL }
            Values = [(1000UL, Monthly)]
            Expected = IncludedQuantity { Annually = None; Monthly = Some 1000UL } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL; Monthly = None } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL ; Monthly = Some 2UL } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL }
            Values = [(1000UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 1000UL ; Monthly = None } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [
                (10_000UL, Annually)
                (500UL, Monthly)
            ]
            Expected = IncludedQuantity { Annually = Some 10_000UL; Monthly = Some 500UL } 
        }
    ]
    |> List.map(fun { Values=values; Input=input; Expected=expected} -> 
        Assert.AreEqual(expected, values |> List.fold MeterValue.topupMonthlyCredits input))
    |> ignore
