
using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

public class FishTankOpenTrack : MonoBehaviour
{
    [Header("UDP")]
    public int port = 4242;

    [Header("Position")]
    public float positionScale = 0.01f;
    public Vector3 positionSigns = new Vector3(1, 1, 1);

    [Header("Rotation")]
    public Vector3 rotationSigns = new Vector3(1, 1, 1);
    public float rotationSensitivity = 1f;

    [Header("Smoothing")]
    public float positionSmooth = 15f;
    public float rotationSmooth = 15f;

    [Header("Calibration")]
    public KeyCode recenterKey = KeyCode.R;

    public Vector3 receivedPosition;
    public Vector3 receivedRotation;

    private UdpClient client;
    private IPEndPoint endpoint;
    private readonly object sync = new object();

    private Vector3 netPos, netRot;
    private Vector3 neutralPos, neutralRot;
    private Vector3 smoothPos;
    private Quaternion smoothRot = Quaternion.identity;

    private bool hasData, calibrated, shuttingDown;

    private Vector3 startPosition;
    private Quaternion startRotation;

    void Start()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;

        try
        {
            client = new UdpClient(port);
            endpoint = new IPEndPoint(IPAddress.Any, 0);
            client.BeginReceive(Receive, null);
            Debug.Log("OpenTrack listening on port " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("OpenTrack UDP error: " + e.Message);
        }
    }

    void Receive(IAsyncResult result)
    {
        try
        {
            if (shuttingDown || client == null) return;

            byte[] data = client.EndReceive(result, ref endpoint);

            if (data.Length >= 48)
            {
                // Standard OpenTrack UDP: X, Y, Z, Yaw, Pitch, Roll
                Vector3 p = new Vector3(
                    (float)BitConverter.ToDouble(data, 0),
                    (float)BitConverter.ToDouble(data, 8),
                    (float)BitConverter.ToDouble(data, 16));

                Vector3 r = new Vector3(
                    (float)BitConverter.ToDouble(data, 32), // Pitch
                    (float)BitConverter.ToDouble(data, 24), // Yaw
                    (float)BitConverter.ToDouble(data, 40)); // Roll

                if (Finite(p) && Finite(r))
                {
                    lock (sync)
                    {
                        netPos = p;
                        netRot = r;
                        hasData = true;
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception e)
        {
            if (!shuttingDown)
                Debug.LogWarning("OpenTrack: " + e.Message);
        }
        finally
        {
            if (!shuttingDown && client != null)
            {
                try { client.BeginReceive(Receive, null); }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }
    }

    bool Finite(Vector3 v)
    {
        return Finite(v.x) && Finite(v.y) && Finite(v.z);
    }

    bool Finite(float v)
    {
        return !float.IsNaN(v) && !float.IsInfinity(v);
    }

    void Update()
    {
        Vector3 p, r;
        bool valid;

        lock (sync)
        {
            p = netPos;
            r = netRot;
            valid = hasData;
        }

        if (!valid) return;

        receivedPosition = p;
        receivedRotation = r;

        if (!calibrated ||
            (Keyboard.current != null &&
             Keyboard.current.rKey.wasPressedThisFrame))
        {
            neutralPos = p;
            neutralRot = r;
            calibrated = true;
            Debug.Log("Head tracking recentered");
        }

        Vector3 dp = Vector3.Scale(
            (p - neutralPos) * positionScale, positionSigns);

        Vector3 dr = new Vector3(
            Mathf.DeltaAngle(neutralRot.x, r.x),
            Mathf.DeltaAngle(neutralRot.y, r.y),
            Mathf.DeltaAngle(neutralRot.z, r.z));

        dr = Vector3.Scale(dr * rotationSensitivity, rotationSigns);

        Quaternion targetRot = Quaternion.Euler(dr);

        float pt = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
        float rt = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);

        smoothPos = Vector3.Lerp(smoothPos, dp, pt);
        smoothRot = Quaternion.Slerp(smoothRot, targetRot, rt);

        transform.localPosition = startPosition + smoothPos;
        transform.localRotation = startRotation * smoothRot;
    }

    void OnDestroy()
    {
        shuttingDown = true;
        if (client != null)
        {
            client.Close();
            client = null;
        }
    }
}
