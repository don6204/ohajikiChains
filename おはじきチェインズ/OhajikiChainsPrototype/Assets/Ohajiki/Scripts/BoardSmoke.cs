using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ohajiki
{
    // Explicit --board-smoke-test only. The app owns this coroutine across battle teardown.
    public sealed class BoardSmoke : MonoBehaviour
    {
        PrototypeApp app;BattleTuning template;string output;
        public void Run(PrototypeApp owner,BattleTuning config){app=owner;template=config;StartCoroutine(Guard(Flow()));}
        IEnumerator Guard(IEnumerator flow)
        {
            var stack=new Stack<IEnumerator>();stack.Push(flow);
            while(stack.Count>0)
            {
                object next;
                try
                {
                    if(!stack.Peek().MoveNext()){stack.Pop();continue;}
                    next=stack.Peek().Current;if(next is IEnumerator child){stack.Push(child);continue;}
                }
                catch(Exception error){Debug.LogError("BOARD SMOKE FAIL: "+error);Application.Quit(27);yield break;}
                yield return next;
            }
        }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        IEnumerator Until(Func<bool> predicate,float timeout,string message)
        {
            float deadline=Time.realtimeSinceStartup+timeout;
            while(!predicate()&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(predicate(),message);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
            ValidateChargeDisplay();
            UiSmokeChecks.Validate(app.Battle.View.gameObject);
            Require(!app.Battle.View.GetComponentsInChildren<TMP_Text>().Any(t=>t.isTextOverflowing),"text overflow "+name);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return null;yield return new WaitForEndOfFrame();
        }
        void ValidateChargeDisplay()
        {
            var view=app.Battle.View;
            var board=view.Board;
            var playerZone=(RectTransform)board.Find("LaunchZone");
            var field=(RectTransform)board.Find("ChargeField");
            float playerBoundary=playerZone.anchoredPosition.y+playerZone.rect.height;
            float fieldTop=field.anchoredPosition.y+field.rect.height;
            Require(field.anchoredPosition.y>playerBoundary+8,"separate shot lane below field");
            var frame = field.Find("BoardFrame")?.GetComponent<PuzzleBoardFrame>();
            Require(frame && frame.isActiveAndEnabled && frame.color.a > 0 && !frame.raycastTarget,
                "puzzle frame is visible and passes pointer input through");
            var frameBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(field, frame.rectTransform);
            Require(Vector2.Distance(frameBounds.min, field.rect.min) < .1f &&
                Vector2.Distance(frameBounds.max, field.rect.max) < .1f, "puzzle frame fits the charge field");
            Require(field.Find("Field surface").GetSiblingIndex() < frame.transform.GetSiblingIndex() &&
                field.GetSiblingIndex() < board.Find("Direction").GetSiblingIndex() &&
                field.GetSiblingIndex() < board.Find("SpiritBall").GetSiblingIndex(), "frame sits above the field surface and below the shot");
            var laneFrame = playerZone.Find("RailFrame")?.GetComponent<PuzzleBoardFrame>();
            Require(laneFrame && laneFrame.isActiveAndEnabled && !laneFrame.raycastTarget,
                "shot lane uses the same visible, non-intercepting frame as the puzzle board");
            var laneFrameBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(playerZone, laneFrame.rectTransform);
            Require(Vector2.Distance(laneFrameBounds.min, playerZone.rect.min) < .1f &&
                Vector2.Distance(laneFrameBounds.max, playerZone.rect.max) < .1f, "frame follows the full shot lane");
            var track = (RectTransform)playerZone.Find("Shot position track");
            float laneInset = PuzzleBoardFrame.Inset * laneFrame.rectTransform.localScale.x;
            Rect interior = Rect.MinMaxRect(laneInset, laneInset, playerZone.rect.width - laneInset, playerZone.rect.height - laneInset);
            foreach (var content in playerZone.GetComponentsInChildren<TMP_Text>().Select(t => t.rectTransform).Append(track))
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(playerZone, content);
                Require(interior.Contains(bounds.min) && interior.Contains(bounds.max), "lane trim clears " + content.name);
            }
            var corners=new Vector3[4];
            var cells=app.Battle.Simulation.Charges;
            for(int i=0;i<cells.Length;i++)
            {
                var cell=cells[i];
                var charge=(RectTransform)board.Find("Charge_"+i);
                Require(charge && charge.gameObject.activeSelf==cell.Active,"charge visibility at cell "+i);
                if(!cell.Active)continue;
                var expected=template.GetChargeSprite(cell.Special,cell.Element);
                var image=charge.GetComponent<Image>();
                var fallback=charge.Find("ChargeFallback");
                string kind=$"{cell.Element}/{cell.Special} at cell {i}";
                Require(image && image.isActiveAndEnabled,"missing charge image for "+kind);
                bool fallbackVisible=fallback && fallback.gameObject.activeInHierarchy;
                Require(fallbackVisible==(expected==null),"charge fallback visibility for "+kind);
                if(expected!=null)
                    Require(image.overrideSprite==expected && image.color==Color.white,"master artwork for "+kind);
                var visibleBounds=expected!=null ? charge : (RectTransform)fallback.Find("Orb rim");
                visibleBounds.GetWorldCorners(corners);
                var local=corners.Select(p=>board.InverseTransformPoint(p)).ToArray();
                Require(local.Min(p=>p.y)>playerBoundary+8,"charge stays outside the full player shot area: "+kind);
                Require(charge.GetSiblingIndex() > field.GetSiblingIndex(), "charge drawn above puzzle frame: "+kind);
                Require(local.Min(p=>p.y)>field.anchoredPosition.y+PuzzleBoardFrame.Inset && local.Max(p=>p.y)<fieldTop-PuzzleBoardFrame.Inset,"charge clears top and bottom frame: "+kind);
                Require(local.Min(p=>p.x)>field.anchoredPosition.x+PuzzleBoardFrame.Inset && local.Max(p=>p.x)<field.anchoredPosition.x+field.rect.width-PuzzleBoardFrame.Inset,"charge clears side frame: "+kind);
                Vector2 center=(local[0]+local[2])*.5f;
                Require(Vector2.Distance(center,view.ProjectBoardPoint(cell.Position))<.1f,"art centered on logical cell: "+kind);
                Require(Vector2.Distance(cell.Position,view.UnprojectBoardPoint(center))<.1f,"inverse board projection: "+kind);
                if(i==0 && expected!=null)
                {
                    var nextColumn = (RectTransform)board.Find("Charge_1");
                    var nextRow = (RectTransform)board.Find("Charge_" + template.boardColumns);
                    Vector2 columnCenter = board.InverseTransformPoint(nextColumn.TransformPoint(nextColumn.rect.center));
                    Vector2 rowCenter = board.InverseTransformPoint(nextRow.TransformPoint(nextRow.rect.center));
                    float horizontalPitch = Mathf.Abs(columnCenter.x - center.x);
                    float verticalPitch = Mathf.Abs(rowCenter.y - center.y);
                    float width = local[2].x - local[0].x;
                    float height = local[2].y - local[0].y;
                    Require(Mathf.Abs(width - height) < .1f, "charge artwork stays square");
                    Require(width > Mathf.Min(horizontalPitch, verticalPitch) * .75f, "icon fills the shorter side of a grid cell");
                    Require(width < horizontalPitch && height < verticalPitch, "icons have horizontal and vertical gaps");
                }
            }
        }
        void CheckShotLaneInput(PrototypeBattle battle)
        {
            var view=battle.View;
            var sim=battle.Simulation;
            Vector2 ScreenPoint(Vector2 world) => RectTransformUtility.WorldToScreenPoint(null,view.Board.TransformPoint(view.ProjectBoardPoint(world)));
            var start=new Vector2(ShotSimulation.Width*.24f,sim.Position.y);
            var aim=view.Board.Find("Direction");
            var lane = (RectTransform)view.Board.Find("LaunchZone");
            Vector2 trimPoint = RectTransformUtility.WorldToScreenPoint(null, lane.TransformPoint(new Vector2(lane.rect.width * .32f, 3f)));
            Vector2 trimDragEnd = start + Vector2.right * 90;
            battle.HandleShotPointer(trimPoint, true, false);
            battle.HandleShotPointer(ScreenPoint(trimDragEnd), false, true);
            Require(Vector2.Distance(trimDragEnd, sim.Position) < .1f && !sim.Moving, "drag can start on the decorative lane trim");
            battle.HandleShotPointer(ScreenPoint(start), true, false);
            var midway=start+Vector2.right*100;
            battle.HandleShotPointer(ScreenPoint(midway), false, false);
            Require(Vector2.Distance(midway,sim.Position)<.1f,"rail follows pointer before release");
            var inset=template.shotRadius+MasterConstants.GetFloat("ShotSimulation.launchSidePadding",18);
            battle.HandleShotPointer(ScreenPoint(start+Vector2.left*1000), false, false);
            Require(Mathf.Abs(sim.Position.x-inset)<.1f,"left end clamps the launch ball");
            battle.HandleShotPointer(ScreenPoint(start+Vector2.right*1000), false, false);
            Require(Mathf.Abs(sim.Position.x-(ShotSimulation.Width-inset))<.1f,"right end clamps the launch ball");
            battle.HandleShotPointer(ScreenPoint(start), false, true);
            Require(Vector2.Distance(start,sim.Position)<.1f && !sim.Moving,"release fixes launch position");
            battle.HandleShotPointer(ScreenPoint(start), true, false);
            battle.HandleShotPointer(ScreenPoint(midway), false, false);
            Require(Vector2.Distance(midway,sim.Position)<.1f && !aim.gameObject.activeSelf,"horizontal knob drag repositions instead of aiming");
            var fieldPoint=midway+new Vector2(40,180);
            battle.HandleShotPointer(ScreenPoint(fieldPoint), false, false);
            Require(Mathf.Abs(sim.Position.x-fieldPoint.x)<.1f && Mathf.Abs(sim.Position.y-midway.y)<.1f && !aim.gameObject.activeSelf,
                "after horizontal drag leaves the lane, ball keeps following X without switching to a shot");
            battle.HandleShotPointer(ScreenPoint(fieldPoint), false, true);
            Require(!sim.Moving,"releasing a horizontal drag outside the lane does not fire");
            midway=sim.Position;
            battle.HandleShotPointer(ScreenPoint(fieldPoint), true, false);
            battle.HandleShotPointer(ScreenPoint(fieldPoint), false, true);
            Require(Vector2.Distance(midway,sim.Position)<.1f && !sim.Moving,"independent press on field is ignored");
            CheckFreeShot(battle, false);
            CheckFreeShot(battle, true);
            battle.Restart();
            Debug.Log("SHOT INPUT PASS: continuous rail drag, end clamps, horizontal placement, forward and backward aim and launch.");
        }
        static Vector2 ScreenCenter(RectTransform visual) =>
            RectTransformUtility.WorldToScreenPoint(null, visual.TransformPoint(visual.rect.center));

        void CheckFreeShot(PrototypeBattle battle, bool rearward)
        {
            battle.Restart();
            var sim = battle.Simulation;
            var view = battle.View;
            foreach (var charge in sim.Charges) charge.Active = false;
            var ball = (RectTransform)view.Board.Find("SpiritBall");
            var guide = view.Board.Find("Direction");
            Vector2 heading = new Vector2(.27f, rearward ? -1 : 1).normalized;
            Vector2 start = ScreenCenter(ball);
            Vector2 finish = start + heading * 130;
            battle.HandleShotPointer(start, true, false);
            battle.HandleShotPointer(finish, false, false);
            var lines = guide.GetComponentsInChildren<Image>();
            Require(guide.gameObject.activeInHierarchy && lines.Length > 0, "swipe shows an aim line");
            foreach (var line in lines)
            {
                var rect = line.rectTransform;
                Vector2 rightEnd = RectTransformUtility.WorldToScreenPoint(null,
                    rect.TransformPoint(rect.rect.center + Vector2.right * (rect.rect.width / 2)));
                Require(Vector2.Angle(rightEnd - ScreenCenter(rect), heading) < .25f, "aim follows the screen-space swipe");
            }
            battle.HandleShotPointer(finish, false, true);
            Require(sim.Moving && !guide.gameObject.activeSelf, "release launches the guided shot");

            float firstScale = 0, maxAngleError = 0, maxScaleError = 0;
            bool crossedBottom = false, crossedTop = false;
            for (int step = 0; step < 600 && sim.Moving; step++)
            {
                Vector2 before = sim.Position, screenBefore = ScreenCenter(ball);
                float beforeY = sim.Velocity.y;
                sim.Tick(1f / 240);
                view.UpdateSimulationVisuals(sim, battle.PlayerCard);
                if (beforeY > 0 && sim.Velocity.y < 0) { crossedTop = true; break; }
                if (beforeY < 0 && sim.Velocity.y > 0)
                {
                    crossedBottom = true;
                    heading.y = -heading.y;
                    continue; // This integration step includes the wall contact.
                }
                Vector2 screenStep = ScreenCenter(ball) - screenBefore;
                float physicalDistance = Vector2.Distance(before, sim.Position);
                Require(physicalDistance > 0 && screenStep.sqrMagnitude > 0, "moving spirit advances on screen");
                float scale = screenStep.magnitude / physicalDistance;
                if (firstScale == 0) firstScale = scale;
                maxScaleError = Mathf.Max(maxScaleError, Mathf.Abs(scale / firstScale - 1));
                maxAngleError = Mathf.Max(maxAngleError, Vector2.Angle(screenStep, heading));
            }
            Require(crossedTop && crossedBottom == rearward, "shot reaches the opposite wall after the expected reflections");
            Debug.Log($"TRAJECTORY {(rearward ? "rearward" : "forward")}: heading error {maxAngleError:F3} degrees; display scale variation {maxScaleError:P3}");
            Require(maxAngleError < .25f, "free shot stays aligned with the swipe between wall impacts");
            Require(maxScaleError < .005f, "equal physical displacements keep the same screen length across the lane and field");

            // Follow the return trip in the rendered field, including the bottom wall by the launch lane.
            var field = (RectTransform)view.Board.Find("ChargeField");
            var inside = field.rect;
            float inset = PuzzleBoardFrame.Inset - .1f;
            inside = Rect.MinMaxRect(inside.xMin + inset, inside.yMin + inset, inside.xMax - inset, inside.yMax - inset);
            var corners = new Vector3[4];
            bool reboundedInside = false;
            for (int step = 0; step < 600 && sim.Moving && !reboundedInside; step++)
            {
                ball.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector2 local = field.InverseTransformPoint(corner);
                    Require(inside.Contains(local), "spirit ball artwork remains inside the puzzle rim on its return");
                }
                float incomingY = sim.Velocity.y;
                sim.Tick(1f / 240);
                view.UpdateSimulationVisuals(sim, battle.PlayerCard);
                reboundedInside = incomingY < 0 && sim.Velocity.y > 0;
            }
            Require(reboundedInside, "an entered shot rebounds above the launch lane");
            ball.GetWorldCorners(corners);
            float bottom = field.InverseTransformPoint(corners[0]).y;
            Require(Mathf.Abs(bottom - field.rect.yMin - PuzzleBoardFrame.Inset) < 8f,
                "lower bounce aligns with the visible inner rim");
        }
        IEnumerator CaptureChargeVariants(PrototypeBattle battle)
        {
            Require(app.Master && app.Master.battle==template,"board uses the imported battle master");
            var elements=(Element[])Enum.GetValues(typeof(Element));
            var effects=(SpecialCharge[])Enum.GetValues(typeof(SpecialCharge));
            var cells=battle.Simulation.Charges;
            foreach(var cell in cells)cell.Active=false;
            int assigned=0;
            int firstRow=(template.boardRows-elements.Length)/2;
            for(int row=0;row<elements.Length;row++)
                for(int column=0;column<effects.Length;column++)
                {
                    var cell=cells[(firstRow+elements.Length-1-row)*template.boardColumns+column];
                    cell.Element=elements[row];cell.Special=effects[column];cell.Active=true;
                    if(template.GetChargeSprite(cell.Special,cell.Element)!=null)assigned++;
                }
            battle.Select(battle.SelectedSlot);
            yield return Capture("02-charge-variants");
            Debug.Log($"CHARGE ART: {assigned}/{elements.Length*effects.Length} master sprites assigned; all combinations verified on board.");
            battle.Restart();
        }
        IEnumerator CaptureRocketSweep(PrototypeBattle battle)
        {
            var sim = battle.Simulation;
            var view = battle.View;
            var source = battle.PlayerCard;
            var originalAction = source.actionSkill;
            int columns = template.boardColumns;
            const int row = 3, launchColumn = 2, chainColumn = 5;
            int horizontal = row * columns + launchColumn;
            int vertical = row * columns + chainColumn;
            string directory = Path.Combine(output, "RocketSweep");
            Directory.CreateDirectory(directory);
            // Step an actual shot at fixed intervals, independent of the screenshot writer's speed.
            battle.enabled = false;
            try
            {
                source.actionSkill = ActionSkill.None;
                for (int i = 0; i < sim.Charges.Length; i++)
                {
                    sim.Charges[i].Active = i % columns != launchColumn || i / columns >= row;
                    sim.Charges[i].Special = SpecialCharge.None;
                }
                sim.Charges[horizontal].Element = Element.Fire;
                sim.Charges[horizontal].Special = SpecialCharge.HorizontalMissile;
                sim.Charges[vertical].Element = Element.Water;
                sim.Charges[vertical].Special = SpecialCharge.VerticalMissile;
                sim.SetLaunchX(sim.Charges[horizontal].Position.x);
                battle.Select(battle.SelectedSlot);
                Require(battle.Shoot(Vector2.up * 300), "rocket sweep launch");
                for (int i = 0; i < 300 && sim.Rockets.Count == 0; i++) sim.Tick(.002f);
                Require(!sim.Charges[horizontal].Active && sim.Charges[vertical].Active && sim.Rockets.Count == 2,
                    "horizontal rockets start before the distant vertical charge is reached");
                bool left = false, right = false, up = false, down = false, partialRow = false, partialColumn = false;
                var layer = (RectTransform)view.Feedback.transform.Find("Rocket flights");
                Require(layer.GetComponent<RectMask2D>() && view.Feedback.transform.Find("Damage counter").GetSiblingIndex() > layer.GetSiblingIndex() &&
                    view.Feedback.transform.Find("Contact burst").GetSiblingIndex() < layer.GetSiblingIndex(),
                    "field-clipped rockets draw above contact bursts and below the damage display");
                for (int frame = 0; frame < 19; frame++)
                {
                    view.UpdateSimulationVisuals(sim, source);
                    var visuals = layer.GetComponentsInChildren<ChargeRocketGraphic>();
                    Require(visuals.Length == sim.Rockets.Count, "one visible rocket for each active flight");
                    for (int i = 0; i < visuals.Length; i++)
                    {
                        var rocket = sim.Rockets[i];
                        var graphic = visuals[i];
                        Vector2 nose = view.Board.InverseTransformPoint(graphic.rectTransform.position);
                        Vector2 direction = view.Board.InverseTransformDirection(graphic.transform.right);
                        Require(Vector2.Distance(nose, view.ProjectBoardPoint(rocket.Position)) < .1f &&
                            Vector2.Dot(direction, rocket.Direction) > .999f && !graphic.raycastTarget,
                            "rocket nose and heading follow the simulated sweep");
                        left |= rocket.Direction == Vector2.left;
                        right |= rocket.Direction == Vector2.right;
                        up |= rocket.Direction == Vector2.up;
                        down |= rocket.Direction == Vector2.down;
                    }
                    partialRow |= !sim.Charges[horizontal + 1].Active && sim.Charges[vertical].Active;
                    partialColumn |= !sim.Charges[vertical + columns].Active && sim.Charges[vertical + 3 * columns].Active;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, frame.ToString("D2") + ".png"));
                    yield return new WaitForSecondsRealtime(.06f);
                    sim.Tick(.06f);
                }
                Require(left && right && up && down && partialRow && partialColumn, "both flight axes clear cells in sequence");
            }
            finally
            {
                source.actionSkill = originalAction;
                battle.enabled = true;
                battle.Restart();
            }
            Require(view.Feedback.GetComponentsInChildren<ChargeRocketGraphic>().Length == 0, "retry clears rocket visuals");
        }

        static void PlaceCascade(PrototypeBattle battle)
        {
            foreach(var c in battle.Simulation.Charges){c.Active=true;c.Element=battle.PlayerCard.element;c.Special=SpecialCharge.VerticalMissile;}
            battle.Simulation.Charges[0].Special=SpecialCharge.HorizontalMissile;
            battle.Simulation.Charges[9].Special=SpecialCharge.HorizontalMissile;
            battle.PlayerCard.actionSkill=ActionSkill.None;
            battle.Simulation.SetLaunchX(52);
        }
        IEnumerator Flow()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeBoard"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.8f);
            // Hidden Windows smoke launches can have a black initial backbuffer until the first resize.
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            var battle=app.Battle;var sim=battle.Simulation;var feedback=battle.View.Feedback;
            Require(template.boardColumns==8&&template.boardRows==8&&sim.Charges.Length==64,"8 x 8 authored board");
            Require(battle.View.Board.GetComponentsInChildren<RectTransform>().Count(r=>r.name.StartsWith("Charge_",StringComparison.Ordinal))==64,"64 visible charge cells");
            CheckShotLaneInput(battle);
            yield return Capture("01-board-540x960");
            yield return CaptureChargeVariants(battle);
            yield return CaptureRocketSweep(battle);
            foreach(var size in new[]{new Vector2Int(480,800),new Vector2Int(720,900)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSeconds(.25f);
                yield return Capture("02-board-"+size.x+"x"+size.y);
            }
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.25f);
            // A deterministic first shot clears all 64 via missile chaining before the delay starts.
            // This smoke owns its temporary tuning/cards and destroys them after battle teardown.
            var durable=Instantiate(template);durable.deceleration=1;durable.maximumShotSeconds=14;
            durable.playerDeck=template.playerDeck.Select(s=>Instantiate(s)).ToArray();
            durable.enemyDeck=template.enemyDeck.Select(s=>Instantiate(s)).ToArray();
            // A board/refill fixture must survive its forced 64-hit missile cascade,
            // including when this build delivers damage immediately on contact.
            foreach(var spirit in durable.playerDeck.Concat(durable.enemyDeck))spirit.baseHp=50000;
            durable.specialChargeProbabilities=new float[6];
            battle.ConfigureSession(durable,null,app.Session.Progress);
            battle.Restart();sim=battle.Simulation;PlaceCascade(battle);
            Require(battle.Shoot(Vector2.up*300),"cascade launch");
            yield return Until(()=>sim.AllClearCount==1,4,"all-clear fires once after the rockets reach the last cells");
            Require(sim.Collected==64&&sim.Charges.All(c=>!c.Active),"all 64 consumed in one chain");
            yield return Until(()=>sim.AllClearWaitSeconds>=.65f,2,"banner reaches center");
            Require(feedback.AllClearVisible,"gold banner appears");
            var label=battle.View.GetComponentsInChildren<TMP_Text>().Single(t=>t.name=="AllClearText");
            Require(label.text=="全消し"&&label.color.r>.95f&&label.color.g>.7f&&label.color.b<.3f,"gold Japanese all-clear label");
            float x=feedback.AllClearPosition.x;yield return new WaitForSeconds(.08f);
            Require(feedback.AllClearPosition.x<x,"banner scrolls right to left");
            battle.TogglePause();float pausedAge=sim.AllClearWaitSeconds;var pausedPosition=feedback.AllClearPosition;
            yield return Capture("03-all-clear-gold");yield return new WaitForSeconds(.3f);
            Require(sim.AllClearWaitSeconds==pausedAge&&feedback.AllClearPosition==pausedPosition,"pause freezes refill and banner");
            battle.TogglePause();
            yield return Until(()=>!feedback.AllClearVisible,2,"banner completes");
            Require(sim.AllClearWaitSeconds>=1&&sim.AllClearWaitSeconds<=2,"banner lasts between one and two game seconds");
            yield return Until(()=>sim.AllClearWaitSeconds>=4.8f,5,"near five-second threshold");
            Require(sim.ResetCount==1&&sim.Charges.All(c=>!c.Active),"no early refill");
            yield return Until(()=>sim.ResetCount==2,1,"moving shot refills after five seconds");
            Require(sim.Moving&&sim.Collected>=64&&sim.Matched>=64,"refill preserves moving shot and accumulated hits");
            Require(battle.View.Board.GetComponentsInChildren<RectTransform>().Any(r=>r.name.StartsWith("Charge_",StringComparison.Ordinal)),"refilled balls become visible immediately");
            yield return Capture("04-refilled-mid-shot");
            battle.Restart();Require(!feedback.AllClearVisible&&!battle.Simulation.AwaitingAllClearRefill,"retry clears banner and timer");
            durable.maximumShotSeconds=1;PlaceCascade(battle);
            Require(battle.Shoot(Vector2.up*300),"short-shot launch");
            yield return Until(()=>!battle.Simulation.Moving,3,"shot ends before delay");
            Require(battle.Simulation.ResetCount==1&&!battle.Simulation.AwaitingAllClearRefill,"stopped shot has no delayed refill");
            app.ReturnHome();foreach(var spirit in durable.playerDeck.Concat(durable.enemyDeck))Destroy(spirit);Destroy(durable);
            yield return new WaitForSeconds(.3f);
            Require(FindObjectsByType<BattleFeedback>(FindObjectsSortMode.None).Length==0,"return destroys all-clear UI");
            Debug.Log("BOARD SMOKE PASS: framed 64-cell board with clear charge artwork and unobstructed input at 3 resolutions, missile-chain all-clear once, gold right-to-left banner 1-2 seconds, pause, five-second moving refill with visible cells and preserved hits, stopped-shot cancellation, retry and return cleanup.");
            Application.Quit(0);
        }
    }
}
