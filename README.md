# FSRS-Sharp
[![NuGet Downloads](https://img.shields.io/nuget/v/Fsrs.Sharp)](https://www.nuget.org/packages/Fsrs.Sharp/1.0.0)

FSRS-Sharp is a .NET port of the [Python library](https://github.com/open-spaced-repetition/py-fsrs/tree/main) implementing [Free Spaced Repetition Algorithm](https://github.com/open-spaced-repetition/free-spaced-repetition-scheduler)

## Quick Start

#### Create Card
```csharp
var card = new Card();
```
#### Create Scheduler
```csharp
var scheduler = new Scheduler();
```
#### Rate and review the card

- **Rating.Again**: forgot the card
- **Rating.Hard**: remembered the card with serious difficulty
- **Rating.Good**: remembered the card after a hesitation
- **Rating.Easy**: remembered the card easily
```csharp
var rating = Rating.Good;
var reviewResult = scheduler.ReviewCard(card, rating);
var reviewedCard = reviewResult.Card;
```
#### See when the card is due next
```csharp
var due = reviewedCard.Due;
var timeSpan = due - DateTime.UtcNow;
Console.WriteLine($"Card due on {due}"); // Card due on 1/3/2026 12:35:43 PM
Console.WriteLine($"Card due in {timeSpan.TotalSeconds} seconds"); // Card due in 599 seconds
```

### Retrievability
```csharp
var retrievability = scheduler.GetCardRetrievability(reviewedCard);
Console.WriteLine($"There is a {retrievability} probability that this card is remembered."); // There is a 0.999 probability that this just reviewed card is remembered
```

## Custom Parameters
```csharp
var scheduler = new Scheduler(new FsrsConfig
{
    DesiredRetention = 0.8,
    EnableFuzzing = false,
    MaximumInterval = 36500,
    LearningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)],
    RelearningSteps = [TimeSpan.FromMinutes(10)], 
    Parameters = new FsrsParameters() // we can configure Weights here
});
```
`Parameters.Weights` are a set of 21 model weights that affect how the FSRS scheduler will schedule future reviews. If you're not familiar with optimizing FSRS, it is best not to modify these default values.

`DesiredRetention` is a value between 0 and 1 that sets the desired minimum retention rate for cards when scheduled with the scheduler. For example, with the default value of DesiredRetention=0.9, a card will be scheduled at a time in the future when the predicted probability of the user correctly recalling that card falls to 90%. A higher desired_retention rate will lead to more reviews and a lower rate will lead to fewer reviews.

`LearningSteps` are custom time intervals that schedule new cards in the Learning state. By default, cards in the Learning state have short intervals of 1 minute then 10 minutes. You can also disable LearningSteps with learning_steps=[]

`RelearningSteps` are analogous to learning_steps except they apply to cards in the Relearning state. Cards transition to the Relearning state if they were previously in the Review state, then were rated Again - this is also known as a 'lapse'. If you specify Scheduler relearning_steps=[], cards in the Review state, when lapsed, will not move to the Relearning state, but instead stay in the Review state.

`MaximumInterval` sets the cap for the maximum days into the future the scheduler is capable of scheduling cards. For example, if you never want the scheduler to schedule a card more than one year into the future, you'd set Scheduler(maximum_interval=365).

`EnableFuzzing`, if set to True, will apply a small amount of random 'fuzz' to calculated intervals. For example, a card that would've been due in 50 days, after fuzzing, might be due in 49, or 51 days


### Optimizer 
**Not supported yet**

## References
- [Python package](https://github.com/open-spaced-repetition/py-fsrs/tree/main)
- [Community](https://github.com/open-spaced-repetition)
