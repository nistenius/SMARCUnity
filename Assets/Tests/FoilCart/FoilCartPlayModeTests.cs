using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FoilCart;
using NUnit.Framework;
using ROS.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// FoilCart X03 as a SMaRC sim vehicle (SMARCAssets feat/foilcart_IS):
///   SpawnsInScene   every scene with the SMaRC GUI gets a working 'foilcart' (FleetSpawner on GUI.prefab):
///                   tagged robot, plant ready, on the water hullborne, topics under /foilcart/
///   FoilsAt20kn     open water, local test autopilot: take-off, 20 kn at 0.88 m, model compute time
/// Batch: Unity -batchmode -projectPath SMARCUnity -runTests -testPlatform PlayMode -testFilter FoilCart
/// Results are appended to $FOILCART_EVIDENCE/smarc_playmode.txt (default: the Analysis/Unity evidence folder).
/// </summary>
public class FoilCartPlayModeTests
{
    static readonly string Evidence = System.Environment.GetEnvironmentVariable("FOILCART_EVIDENCE")
        ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
                        "Documents/Marine-Robots/FoilCart-X03/Analysis/Unity/evidence/smarc");

    public static IEnumerable<string> Scenes()
    {
        // all scenes under Assets/Scenes whose file references the shared GUI prefab
        const string guiGuid = "GUI.prefab";   // checked at runtime below; here: the curated list
        return new[] { "AskoEmpty", "AskoEvolo", "AskoWaveTest", "AskoCurated", "AskoHydroweb", "AllTheRobots",
                       "KTHTank", "KTHTankHydro", "KTHTankDock", "KTHTankStrips", "KosterKelpit2026Curated",
                       "KristinebergEmpty", "DjuroCurated", "Beckholmen", "Alars" }.Where(s => guiGuid != null);
    }

    static void Log(string line)
    {
        Directory.CreateDirectory(Evidence);
        File.AppendAllText(Path.Combine(Evidence, "smarc_playmode.txt"), line + "\n");
        Debug.Log("[FoilCartTest] " + line);
    }

    static IEnumerator Load(string scene)
    {
#if UNITY_EDITOR
        var path = $"Assets/Scenes/{scene}.unity";
        var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
        while (!op.isDone) yield return null;
#else
        yield return null;
#endif
    }

    [UnityTest, Timeout(300000)]
    public IEnumerator SpawnsInScene([ValueSource(nameof(Scenes))] string scene)
    {
        LogAssert.ignoreFailingMessages = true;      // scene content may log errors without a ROS endpoint
        yield return Load(scene);
        yield return new WaitForSeconds(3f);
        var fc = GameObject.Find("foilcart");
        Assert.NotNull(fc, $"{scene}: no foilcart in the scene");
        Assert.AreEqual("robot", fc.tag);
        var plant = fc.GetComponent<FoilCartPlant>();
        Assert.IsTrue(plant.Ready, "plant not ready");
        var water = plant.WaterLevelAt(plant.baseLink.position);
        var topics = fc.GetComponentsInChildren<ROSBehaviour>(true).Select(b => b.topic).ToList();
        bool namespaced = topics.Where(t => t != "/tf").All(t => t.StartsWith("/foilcart/"));
        Log(string.Format(CultureInfo.InvariantCulture,
            "{0,-26} spawned at {1} heading {2:F0}  mode {3}  CoG above surface {4:F3} m  water level {5:F3}  roll {6:F1} pitch {7:F1}  topics {8} namespaced {9}  model {10:F2} ms/step",
            scene, plant.baseLink.position.ToString("F1"), plant.headingDeg, plant.mode, plant.rideHeight, water,
            plant.rollDeg, plant.pitchDeg, topics.Count, namespaced, plant.stepMsMean));
        Assert.IsTrue(namespaced, "topics: " + string.Join(", ", topics));
        Assert.AreEqual(FoilCartPlant.Mode.Hullborne, plant.mode);
        Assert.That(plant.rideHeight, Is.InRange(0.2, 0.4), "hullborne CoG height above the surface");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator FoilsAt20kn()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return Load("AskoEvolo");
        yield return new WaitForSeconds(1f);
        var fc = GameObject.Find("foilcart");
        Assert.NotNull(fc);
        var plant = fc.GetComponent<FoilCartPlant>();
        var ap = fc.GetComponentInChildren<FoilCartLocalAutopilot>(true);
        ap.targetKn = 20; ap.keyboard = false; ap.enabled = true;
        var rows = new List<string> { "t,mode,speed_kn,ride_height,roll_deg,pitch_deg,heading_deg,elevator,rudder,prop,prop_diff,step_ms" };
        float t0 = Time.time;
        while (Time.time - t0 < 40f)
        {
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2:F3},{3:F4},{4:F3},{5:F3},{6:F2},{7:F3},{8:F3},{9:F2},{10:F4},{11:F3}",
                Time.time - t0, plant.mode, plant.speedKn, plant.rideHeight, plant.rollDeg, plant.pitchDeg, plant.headingDeg,
                plant.elevator, plant.rudder, plant.prop, plant.propDiff, plant.stepMs));
            yield return new WaitForFixedUpdate();
        }
        Directory.CreateDirectory(Evidence);
        File.WriteAllLines(Path.Combine(Evidence, "foils_at_20kn_AskoEvolo.csv"), rows);
        Capture(fc, Path.Combine(Evidence, "foils_at_20kn_AskoEvolo.png"));
        Log(string.Format(CultureInfo.InvariantCulture,
            "FoilsAt20kn AskoEvolo: mode {0}, {1:F2} kn, CoG above surface {2:F3} m, roll {3:F2}, pitch {4:F2}; touch-downs: '{5}'; model {6:F2} ms mean, {7:F2} ms max per 20 ms physics step; Unity fixed dt {8}",
            plant.mode, plant.speedKn, plant.rideHeight, plant.rollDeg, plant.pitchDeg, plant.lastTouchdownReason,
            plant.stepMsMean, plant.stepMsMax, Time.fixedDeltaTime));
        Assert.AreEqual(FoilCartPlant.Mode.Foiling, plant.mode);
        Assert.That(plant.speedKn, Is.InRange(19.0, 21.0));
        Assert.That(plant.rideHeight, Is.InRange(0.6, 1.1));
    }

    /// <summary>
    /// End to end with the FoilCart VM (run by scripts/smarc_e2e.sh): Unity connects to the VM's ros_tcp_endpoint
    /// (~/SMaRCUnity/Settings/ROSSettings.yaml), the VM's foilcart_controllers close the loop over ROS, and a setpoint
    /// schedule on the VM asks for 20 kn north, then east. Nothing in Unity drives the actuators.
    /// Explicit: needs the VM stack; not part of the default run.
    /// </summary>
    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator RosLoopWithVmStack()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return Load("AskoEvolo");
        var fc = GameObject.Find("foilcart");
        Assert.NotNull(fc);
        var plant = fc.GetComponent<FoilCartPlant>();
        var subs = fc.GetComponentsInChildren<FoilCartActuatorCommand_Sub>(true);
        var rows = new List<string> { "t,mode,speed_kn,ride_height,roll_deg,pitch_deg,heading_deg,elevator_cmd,rudder_cmd,prop_cmd,diff_cmd,cmd_hz,step_ms" };
        float t0 = Time.time; double firstCmd = -1;
        while (Time.time - t0 < 90f)
        {
            double hz = subs.Length > 0 ? subs.Min(s => s.receivedFrequency) : 0;
            if (firstCmd < 0 && subs.All(s => s.ReceivedFirstMessage)) firstCmd = Time.time - t0;
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2:F3},{3:F4},{4:F3},{5:F3},{6:F2},{7:F3},{8:F3},{9:F2},{10:F4},{11:F1},{12:F3}",
                Time.time - t0, plant.mode, plant.speedKn, plant.rideHeight, plant.rollDeg, plant.pitchDeg, plant.headingDeg,
                plant.elevatorCmd, plant.rudderCmd, plant.propCmd, plant.propDiffCmd, hz, plant.stepMs));
            yield return new WaitForFixedUpdate();
        }
        Directory.CreateDirectory(Evidence);
        File.WriteAllLines(Path.Combine(Evidence, "ros_loop_vm3_AskoEvolo.csv"), rows);
        Capture(fc, Path.Combine(Evidence, "ros_loop_vm3_AskoEvolo.png"));
        Log(string.Format(CultureInfo.InvariantCulture,
            "RosLoopWithVmStack AskoEvolo: first VM command after {0:F1} s; end: mode {1}, {2:F2} kn, heading {3:F1}, CoG above surface {4:F3} m, roll {5:F2}; last touch-down '{6}'",
            firstCmd, plant.mode, plant.speedKn, plant.headingDeg, plant.rideHeight, plant.rollDeg, plant.lastTouchdownReason));
        Assert.That(firstCmd, Is.GreaterThanOrEqualTo(0), "no commands from the VM");
        // schedule (smarc_e2e.sh): 20 kn north from 0 s, east from 25 s, ends at 60 s + 15 s hold -> the VM lands it
        string Row(double tt) => rows.Skip(1).First(r => double.Parse(r.Split(',')[0], CultureInfo.InvariantCulture) >= tt);
        double Col(string r, int i) => double.Parse(r.Split(',')[i], CultureInfo.InvariantCulture);
        var r20 = Row(20); var r60 = Row(60);
        Assert.AreEqual("Foiling", r20.Split(',')[1], "foiling at 20 s");
        Assert.That(Col(r20, 2), Is.InRange(19.0, 21.0), "20 kn at 20 s");
        Assert.That(Mathf.Abs(Mathf.DeltaAngle((float)Col(r60, 6), 90f)), Is.LessThan(5f), "heading east at 60 s");
        Assert.AreEqual(FoilCartPlant.Mode.Hullborne, plant.mode, "landed after the schedule ended");
    }

    /// <summary>
    /// A WARA-PS mission over the station broker (scripts/smarc_mission.sh): start-tst built by the Data Cube backend
    /// code -> waraps_bridge -> wasp_bt (surface agent) -> move_to -> foilcart_controllers -> Unity. Records 170 s.
    /// </summary>
    [UnityTest, Explicit, Timeout(900000)]
    public IEnumerator MissionWithVmStack()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return Load("AskoEvolo");
        var fc = GameObject.Find("foilcart");
        var plant = fc.GetComponent<FoilCartPlant>();
        var rows = new List<string> { "t,mode,speed_kn,ride_height,roll_deg,heading_deg,x_east,z_north,elevator_cmd,rudder_cmd,prop_cmd" };
        float t0 = Time.time; float maxKn = 0;
        while (Time.time - t0 < 170f)
        {
            var p = plant.baseLink.position;
            maxKn = Mathf.Max(maxKn, (float)plant.speedKn);
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2:F3},{3:F4},{4:F3},{5:F2},{6:F2},{7:F2},{8:F3},{9:F3},{10:F2}",
                Time.time - t0, plant.mode, plant.speedKn, plant.rideHeight, plant.rollDeg, plant.headingDeg, p.x, p.z,
                plant.elevatorCmd, plant.rudderCmd, plant.propCmd));
            yield return new WaitForFixedUpdate();
        }
        File.WriteAllLines(Path.Combine(Evidence, "mission_mqtt_AskoEvolo.csv"), rows);
        Capture(fc, Path.Combine(Evidence, "mission_mqtt_AskoEvolo.png"));
        Log($"MissionWithVmStack AskoEvolo: max {maxKn:F1} kn, end mode {plant.mode}, position {plant.baseLink.position}, heading {plant.headingDeg:F0}");
        Assert.That(maxKn, Is.GreaterThan(15f), "never foiled");
    }

    /// <summary>Replay of the simulator's coordinated 20 deg turn on the SMaRC vehicle: banks into the turn in Unity.</summary>
    [UnityTest, Timeout(300000)]
    public IEnumerator ReplayTurnBanksIntoTurn()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return Load("AskoEvolo");
        var fc = GameObject.Find("foilcart");
        var plant = fc.GetComponent<FoilCartPlant>();
        var rp = fc.GetComponentInChildren<FoilCartReplay>(true);
        rp.runIndex = System.Array.FindIndex(rp.runs, r => r.name == "ctv1_turn_20kn_20deg");
        Assert.That(rp.runIndex, Is.GreaterThanOrEqualTo(0));
        rp.loop = false; rp.enabled = true;
        var bl = plant.baseLink.transform;
        int good = 0, turning = 0; Vector3 prevF = bl.forward; float t0 = Time.time;
        while (Time.time - t0 < 13f)
        {
            yield return new WaitForFixedUpdate();
            var f = bl.forward;
            float turn = Vector3.Cross(prevF, f).y;           // > 0: turning to starboard
            prevF = f;
            if (Mathf.Abs(turn) < 1e-4f) continue;
            turning++;
            if (Mathf.Sign(turn) == -Mathf.Sign(bl.right.y)) good++;   // inner (lower) side = turn side
        }
        Capture(fc, Path.Combine(Evidence, "replay_turn_AskoEvolo.png"));
        Log($"ReplayTurnBanksIntoTurn AskoEvolo: {good}/{turning} turning frames with the inner side low; roll at end {plant.rollDeg:F1} deg, heading {plant.headingDeg:F0}");
        Assert.That(turning, Is.GreaterThan(100));
        Assert.That(good, Is.GreaterThanOrEqualTo((int)(0.95 * turning)));
    }

    /// <summary>A chase view of the vehicle into a PNG (HDRP renders through Camera.Render into a RenderTexture).</summary>
    static void Capture(GameObject fc, string file)
    {
        var bl = fc.transform.Find("base_link");
        var go = new GameObject("test camera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 40; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000;
        var fwd = bl.forward; fwd.y = 0; fwd.Normalize();
        go.transform.position = bl.position - fwd * 9f + Vector3.up * 3f + Vector3.Cross(Vector3.up, fwd) * 4f;
        go.transform.LookAt(bl.position + Vector3.down * 0.4f);
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.Destroy(go);
    }
}
