using System;
using System.Numerics;
using UseItemsAnywhere.BackpackAccess;

foreach (var side in new[] { "L", "R" })
{
    for (var finger = 1; finger <= 5; finger++)
    {
        for (var joint = 1; joint <= 3; joint++)
        {
            Check(BackpackHandBoneNames.TryGetDigit($"Base Human{side}Digit{finger}{joint}", out var actualFinger, out var actualJoint)
                && actualFinger == finger && actualJoint == joint, "Maps native finger chains", 0f);
        }
    }
    Check(BackpackHandBoneNames.IsIndex($"Base Human{side}Digit21"), "Recognizes Tarkov index knuckle", 0f);
    Check(BackpackHandBoneNames.IsLittle($"Base Human{side}Digit51"), "Recognizes Tarkov little knuckle", 0f);
    foreach (var other in new[] { "Digit11", "Digit22", "Digit52", "Forearm1", "Palm" })
    {
        Check(!BackpackHandBoneNames.IsIndex($"Base Human{side}{other}")
            && !BackpackHandBoneNames.IsLittle($"Base Human{side}{other}"), "Rejects unrelated bones", 0f);
    }
}
foreach (var invalid in new[] { "Base HumanRPalm", "Base HumanRDigit01", "Base HumanRDigit61", "Base HumanRDigit24", "Base HumanRDigit211", "DigitA1" })
{
    Check(!BackpackHandBoneNames.TryGetDigit(invalid, out _, out _), "Rejects invalid finger joints", 0f);
}
Console.WriteLine("PASS: palm calibration recognizes both hands in Tarkov's skeleton.");

var liftSeconds = BackpackAccessMotion.PutAwayDuration * BackpackWithdrawalPath.ClearTime;
var dropSeconds = BackpackAccessMotion.PutAwayDuration
    * (BackpackWithdrawalPath.EndTime - BackpackWithdrawalPath.ClearTime);
Check(liftSeconds >= 0.59f && liftSeconds <= 0.62f, "Preserves readable extraction pace", 5f);
Check(dropSeconds >= 0.40f && dropSeconds <= 0.47f, "Final drop takes less than half a second", 5f);
Console.WriteLine($"PASS: {liftSeconds:0.000}s lift followed by {dropSeconds:0.000}s drop.");

// Exercise the production timeline without requiring a running Unity player.
foreach (var duration in new[] { 0.1f, 0.5f, 1.5f, 3.4f, 3.7f, 4f, 5f, 15f })
{
    var scale = BackpackAccessMotion.TimeScale(duration);
    BackpackAccessMotion At(float time) => new(time, duration - time, scale);
    var start = At(0f);
    Check(start.Raise == 0f && start.LeftWeight == 0f && start.RightWeight == 0f, "Starts hidden", duration);
    var loweredItem = At(BackpackAccessMotion.LowerItemDuration * scale);
    Check(loweredItem.Raise == 0f && loweredItem.RightWeight == 0f, "Held item lowers first", duration);
    var raisedBag = At((BackpackAccessMotion.LowerItemDuration + BackpackAccessMotion.BringInDuration) * scale);
    Check(raisedBag.Raise > 0.999f && raisedBag.RightWeight < 0.001f, "Bag settles before right hand reaches", duration);

    var exitStart = duration - BackpackAccessMotion.PutAwayDuration * scale;
    var withdrawing = At(exitStart + BackpackAccessMotion.PutAwayDuration * scale * 0.4f);
    Check(withdrawing.Lower == 0f && withdrawing.LeftWeight == 1f, "Bag stays braced during withdrawal", duration);
    var justBeforeClear = At(exitStart + BackpackAccessMotion.PutAwayDuration * scale
        * (BackpackWithdrawalPath.ClearTime - 0.001f));
    Check(justBeforeClear.Lower == 0f && justBeforeClear.LowerRightHand == 0f,
        "Faster drop still waits for the hand to clear the bag", duration);
    var lowering = At(exitStart + BackpackAccessMotion.PutAwayDuration * scale * 0.7f);
    Check(lowering.Lower > 0f && lowering.LeftWeight == 1f, "Support follows lowering bag", duration);
    Check(lowering.ClearHand == 1f && lowering.LowerRightHand > 0f && lowering.LowerRightHand < 1f && lowering.RightWeight == 1f,
        "Retrieved hand continues moving while the bag lowers", duration);
    Check(lowering.ShowRetrievedItem, "Retrieved item stays visible during withdrawal", duration);
    Check(lowering.ElbowTuck > 0.5f, "Elbow is tucking while the hand lowers", duration);
    Check(lowering.PresentItem > 0.8f && lowering.PresentItem < 1f,
        "Wrist roll continues into the cross-body drop", duration);
    var earlyWithdrawal = At(exitStart + BackpackAccessMotion.PutAwayDuration * scale * 0.20f);
    Check(earlyWithdrawal.PresentItem == 0f, "Grip starts clearing before wrist rolls", duration);
    var leavingView = At(exitStart + BackpackAccessMotion.PutAwayDuration * scale * 0.92f);
    Check(leavingView.LowerRightHand > 0.92f && leavingView.RightWeight > 0.999f && leavingView.ShowRetrievedItem,
        "Hand keeps grip and item until nearly lowered out of view", duration);
    Check(leavingView.ElbowTuck == 1f, "Elbow finishes retracting before releasing the arm", duration);
    Check(leavingView.PresentItem == 1f, "Wrist completes its roll before arm release", duration);
    var grasp = At(exitStart);
    Check(grasp.Grasp > 0.999f && grasp.ShowRetrievedItem, "Grasp closes and item appears before withdrawal", duration);
    var end = At(duration);
    Check(end.Lower == 1f && end.LeftWeight == 0f && end.RightWeight == 0f, "Ends fully released", duration);
    Check(!end.ShowRetrievedItem, "Preview disappears before native item use", duration);

    var previous = Values(start);
    for (var step = 1; step <= 10000; step++)
    {
        var time = duration * step / 10000f;
        var motion = At(time);
        var values = Values(motion);
        Check(!motion.ShowRetrievedItem || (motion.Insert > 0.999f && motion.Grasp >= 0.70f),
            "Item never appears before the hand has entered and grasped it", duration);
        for (var i = 0; i < values.Length; i++)
        {
            Check(float.IsFinite(values[i]) && Math.Abs(values[i]) <= 1.00001f, "Bounded finite motion", duration);
            Check(Math.Abs(values[i] - previous[i]) < 0.035f, "No phase-boundary jumps", duration);
        }
        if (time < BackpackAccessMotion.SearchStart * scale || time >= exitStart)
        {
            Check(motion.SearchPress == 0f && motion.SearchLift == 0f && motion.SearchSide == 0f,
                "Search stays between entry and withdrawal", duration);
        }
        Check(motion.Raise + 0.00001f >= previous[0] && motion.Lower + 0.00001f >= previous[6],
            "Entry and exit never reverse", duration);
        previous = values;
    }
    Console.WriteLine($"PASS: {duration:0.0}s access — ordered phases, continuous motion, complete release.");
}

var pressSeen = false;
var liftSeen = false;
var leftSeen = false;
var rightSeen = false;
var pauseSeen = false;
for (var time = BackpackAccessMotion.SearchStart + 0.1f; time < 10f; time += 0.02f)
{
    var m = new BackpackAccessMotion(time, 15f - time, 1f);
    pressSeen |= m.SearchPress > 0.9f;
    liftSeen |= m.SearchLift > 0.5f;
    leftSeen |= m.SearchSide < -0.5f;
    rightSeen |= m.SearchSide > 0.5f;
    pauseSeen |= m.SearchPress < 0.01f && m.SearchLift < 0.01f && Math.Abs(m.SearchSide) < 0.01f;
}
Check(pressSeen && liftSeen && leftSeen && rightSeen && pauseSeen, "Search has distinct strokes, alternating sides, and pauses", 15f);
Console.WriteLine("PASS: search choreography has visible strokes and rests.");

for (var finger = 0; finger < 5; finger++)
{
    var minimum = 1f;
    var maximum = 0f;
    for (var time = 2.5f; time < 7f; time += 0.02f)
    {
        var m = new BackpackAccessMotion(time, 15f - time, 1f);
        var curl = m.RightFingerCurl(finger);
        Check(curl >= 0f && curl <= 1f, "Finger curl is bounded", 15f);
        minimum = Math.Min(minimum, curl);
        maximum = Math.Max(maximum, curl);
    }
    Check(maximum - minimum > 0.35f, "Every right finger visibly flexes during search", 15f);
    var earlyHold = new BackpackAccessMotion(14.1f, 0.9f, 1f).RightFingerCurl(finger);
    var lateHold = new BackpackAccessMotion(14.5f, 0.5f, 1f).RightFingerCurl(finger);
    Check(Math.Abs(earlyHold - lateHold) < 0.0001f && lateHold >= 0.65f,
        "Fingers hold their grip during extraction", 15f);
}
var searchPose = new BackpackAccessMotion(3f, 12f, 1f);
Check(Math.Abs(searchPose.RightFingerCurl(1) - searchPose.RightFingerCurl(4)) > 0.1f,
    "Fingers do not flex in lockstep", 15f);
Console.WriteLine("PASS: individual finger motion, stable extraction grip, and preview visibility.");

// Test the actual spatial blend, including velocity on both sides of the
// clearance point. Scalar easing checks alone missed the old frozen hold.
var from = new Vector3(0.12f, -0.10f, 0.48f);
var clearPoint = new Vector3(0.13f, 0.00f, 0.38f);
var rest = new Vector3(-0.24f, -0.44f, 0.18f);
var tangent = new Vector3(-0.35f, -0.045f, -0.18f);
Vector3 Position(float exit)
{
    var path = new BackpackWithdrawalPath(exit);
    return from * path.StartWeight + clearPoint * path.ClearWeight
        + rest * path.RestWeight + tangent * path.TangentWeight;
}
Check(Vector3.Distance(Position(0f), from) < 0.00001f
    && Vector3.Distance(Position(BackpackWithdrawalPath.ClearTime), clearPoint) < 0.00001f
    && Vector3.Distance(Position(1f), rest) < 0.00001f, "Withdrawal meets start, clearance, and hidden endpoints", 5f);
// A fine derivative sample separates the faster cross-body acceleration
// from a discontinuity at the join.
const float delta = 0.0002f;
var join = BackpackWithdrawalPath.ClearTime;
var incoming = (Position(join) - Position(join - delta)) / delta;
var outgoing = (Position(join + delta) - Position(join)) / delta;
Check(Vector3.Distance(incoming, outgoing) < 0.01f
    && Vector3.Distance(incoming, tangent) < 0.01f && outgoing.Length() > 0.1f,
    "Clearance has matching nonzero velocity, with no frozen hold", 5f);
Check((Position(delta) - Position(0f)).Length() / delta < 0.01f
    && (Position(BackpackWithdrawalPath.EndTime) - Position(BackpackWithdrawalPath.EndTime - delta)).Length() / delta < 0.01f,
    "Withdrawal starts and ends gently", 5f);
for (var t = join + 0.01f; t < BackpackWithdrawalPath.EndTime - 0.01f; t += 0.005f)
{
    var before = Position(t - delta);
    var after = Position(t + delta);
    Check(after.X < before.X && after.Y < before.Y && after.Z < before.Z,
        "Cleared hand crosses left, lowers, and draws inward without reversing", 5f);
}
Check(Position(0.80f).X < 0f, "Hand crosses the centerline before arm release", 5f);
Console.WriteLine("PASS: withdrawal follows a continuous curve through clearance without a stop or reversal.");

foreach (var lengths in new[] { (0.30f, 0.30f), (0.26f, 0.34f), (0.36f, 0.28f) })
{
    var (upper, lower) = lengths;
    var previousReach = BackpackArmGeometry.MaximumReach(upper, lower, 0f);
    Check(Math.Abs(previousReach - (upper + lower) * 0.94f) < 0.00001f,
        "Search retains its original reach", 5f);
    for (var step = 1; step <= 1000; step++)
    {
        var reach = BackpackArmGeometry.MaximumReach(upper, lower, step / 1000f);
        Check(reach <= previousReach && reach > Math.Abs(upper - lower)
            && previousReach - reach < 0.001f, "Retraction folds smoothly within valid limb geometry", 5f);
        previousReach = reach;
    }
    var bend = Math.Acos((previousReach * previousReach - upper * upper - lower * lower)
        / (2f * upper * lower)) * 180f / Math.PI;
    Check(Math.Abs(bend - 75f) < 0.01f, "Fully retracted arm retains a 75-degree elbow bend", 5f);
}
Console.WriteLine("PASS: elbow retraction preserves limb lengths and prevents a straight-arm drop.");

static float[] Values(BackpackAccessMotion m) => new[]
{
    m.Raise, m.LeftWeight, m.RightWeight, m.Seam, m.Insert, m.Exit, m.Lower,
    m.SearchPress, m.SearchLift, m.SearchSide, m.Grasp, m.ClearHand, m.LowerRightHand, m.PresentItem, m.ElbowTuck
};

static void Check(bool condition, string description, float duration)
{
    if (!condition) throw new InvalidOperationException($"{description} failed for {duration}s access.");
}
