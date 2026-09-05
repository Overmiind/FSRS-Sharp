namespace FsrsSharp.Models;

/// <summary>
/// Enum representing the learning state of a Card object.
/// </summary>
/// <remarks>
/// FSRS itself has only Learning, Review and Relearning. <see cref="New"/> is this port's addition, for
/// a card that has been created but never reviewed; the scheduler promotes it to Learning on its first
/// review, so a New card that already carries memory state (imported, or restored from a backup) takes
/// the ordinary Learning path.
///
/// There is deliberately no "Learned"/"Mature" member. Maturity is a property of stability, not of the
/// state machine, so derive it where you display it rather than trying to store it here.
/// </remarks>
public enum State
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
}
