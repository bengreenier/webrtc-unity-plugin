using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace UnityWebrtc
{
    /// <summary>
    /// This is based on https://github.com/bengreenier/node-dss
    /// and SHOULD NOT BE USED FOR PRODUCTION.
    /// 
    /// This is a sample implementation of signalling behaviours.
    /// </summary>
    public class WebrtcSignalControls : MonoBehaviour
    {
        /// <summary>
        /// Utility class for using <see cref="JsonUtility"/> to serialize and deserialize messages
        /// </summary>
        [Serializable]
        private class ServerMessage
        {
            /// <summary>
            /// The type of message
            /// </summary>
            /// <remarks>
            /// Valid values are defined in <see cref="Update"/>
            /// </remarks>
            public string type;

            /// <summary>
            /// The raw data of the message
            /// </summary>
            public string data;

            /// <summary>
            /// An optional field indicating that data is composed of many data pieces
            /// that should be <see cref="string.Split(string[], StringSplitOptions)"/> by this value
            /// </summary>
            public string dataSeparator;
        }

        /// <summary>
        /// The id of the <see cref="PlayerPrefs"/> key that we cache the last connected target id under
        /// </summary>
        private const string kLastTargetId = "lastTargetId";

        /// <summary>
        /// The peer event frontend instance that we will control
        /// </summary>
        [Tooltip("The peer event frontend instance to control")]
        public WebrtcPeerEvents PeerEventsInstance;

        /// <summary>
        /// The text field in which we display the device name
        /// </summary>
        [Tooltip("The text field in which we display the device name")]
        public Text DeviceNameLabel;

        /// <summary>
        /// The text input field in which we accept the target device name
        /// </summary>
        [Tooltip("The text input field in which we accept the target device name")]
        public InputField TargetIdField;

        /// <summary>
        /// The button that generates an offer to a given target
        /// </summary>
        [Tooltip("The button that generates an offer to a given target")]
        public Button CreateOfferButton;

        /// <summary>
        /// The https://github.com/bengreenier/node-dss HTTP service address to connect to
        /// </summary>
        [Tooltip("The node-dss server to connect to")]
        public string HttpServerAddress;

        /// <summary>
        /// The interval (in ms) that the server is polled at
        /// </summary>
        [Tooltip("The interval (in ms) that the server is polled at")]
        public float PollTimeMs = 5f;

        /// <summary>
        /// Internal timing helper
        /// </summary>
        private float timeSincePoll = 0f;
        
        /// <summary>
        /// Internal last poll response status flag
        /// </summary>
        private bool lastGetComplete = true;

        /// <summary>
        /// Unity Engine Start() hook
        /// </summary>
        /// <remarks>
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.Start.html
        /// </remarks>
        private void Start()
        {
            if (PeerEventsInstance == null)
            {
                throw new ArgumentNullException("PeerEventsInstance");
            }

            if (string.IsNullOrEmpty(HttpServerAddress))
            {
                throw new ArgumentNullException("HttpServerAddress");
            }

            if (!HttpServerAddress.EndsWith("/"))
            {
                HttpServerAddress += "/";
            }

            // show device label
            DeviceNameLabel.text = SystemInfo.deviceUniqueIdentifier;

            // if playerprefs has a last target id, autofill the field
            if (PlayerPrefs.HasKey(kLastTargetId))
            {
                TargetIdField.text = PlayerPrefs.GetString(kLastTargetId);
            }

            // bind our handler for creating the offer
            CreateOfferButton.onClick.AddListener(() =>
            {
                // create offer if we were given a real targetId
                if (TargetIdField.text.Length > 0)
                {
                    // cache the targetId in PlayerPrefs so we can autofill it in the future
                    PlayerPrefs.SetString(kLastTargetId, TargetIdField.text);

                    PeerEventsInstance.CreateOffer();
                }
            });

            // bind our handler so when the peer is ready, we can start local av
            PeerEventsInstance.OnPeerReady.AddListener(() =>
            {
                PeerEventsInstance.AddStream(audioOnly: false);
            });

            // bind our handler so when an offer is ready we can write it to signalling
            PeerEventsInstance.OnSdpOfferReadyToSend.AddListener((string offer) =>
            {
                StartCoroutine(PostToServer(new ServerMessage()
                {
                    type = "offer",
                    data = offer
                }));
            });

            // bind our handler so when an answer is ready we can write it to signalling
            PeerEventsInstance.OnSdpAnswerReadyToSend.AddListener((string answer) =>
            {
                StartCoroutine(PostToServer(new ServerMessage()
                {
                    type = "answer",
                    data = answer,
                }));
            });

            // bind our handler so when an ice message is ready we can to signalling
            PeerEventsInstance.OnIceCandiateReadyToSend.AddListener((string candidate, int sdpMlineindex, string sdpMid) =>
            {
                StartCoroutine(PostToServer(new ServerMessage()
                {
                    type = "ice",
                    data = candidate + "|" + sdpMlineindex + "|" + sdpMid,
                    dataSeparator = "|"
                }));
            });
        }

        /// <summary>
        /// Internal helper for sending http data to the dss server using POST
        /// </summary>
        /// <param name="msg">the message to send</param>
        private IEnumerator PostToServer(ServerMessage msg)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
            var www = new UnityWebRequest(HttpServerAddress + "data/" + TargetIdField.text, UnityWebRequest.kHttpVerbPOST);
            www.uploadHandler = new UploadHandlerRaw(data);

            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log("Failure sending message: " + www.error);
            }
        }

        /// <summary>
        /// Internal coroutine helper for receiving http data from the dss server using GET
        /// and processing it as needed
        /// </summary>
        /// <returns>the message</returns>
        private IEnumerator CO_GetAndProcessFromServer()
        {
            var www = UnityWebRequest.Get(HttpServerAddress + "data/" + SystemInfo.deviceUniqueIdentifier);
            yield return www.SendWebRequest();

            if (!www.isNetworkError && !www.isHttpError)
            {
                var json = www.downloadHandler.text;

                var msg = JsonUtility.FromJson<ServerMessage>(json);

                // if the message is good
                if (msg != null)
                {
                    // depending on what type of message we get, we'll handle it differently
                    // this is the "glue" that allows two peers to establish a connection.
                    switch (msg.type)
                    {
                        case "offer":
                            PeerEventsInstance.SetRemoteDescription("offer", msg.data);
                            // if we get an offer, we immediately send an answer
                            PeerEventsInstance.CreateAnswer();
                            break;
                        case "answer":
                            PeerEventsInstance.SetRemoteDescription("answer", msg.data);
                            break;
                        case "ice":
                            // this "parts" protocol is defined above, in PeerEventsInstance.OnIceCandiateReadyToSend listener
                            var parts = msg.data.Split(new string[] { msg.dataSeparator }, StringSplitOptions.RemoveEmptyEntries);
                            PeerEventsInstance.AddIceCandidate(parts[0], int.Parse(parts[1]), parts[2]);
                            break;
                        case "set-peer":
                            // this allows a remote peer to set our text target peer id
                            // it is primarily useful when one device does not support keyboard input
                            //
                            // note: when running this sample on hololens (for example) we may use postman or a similar
                            // tool to use this message type to set the target peer. This is NOT a production-quality solution.
                            TargetIdField.text = msg.data;
                            break;
                        default:
                            Debug.Log("Unknown message: " + msg.type + ": " + msg.data);
                            break;
                    }
                }
            }

            lastGetComplete = true;
        }

        /// <summary>
        /// Unity Engine Update() hook
        /// </summary>
        /// <remarks>
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.Update.html
        /// </remarks>
        private void Update()
        {
            // if we have not reached our PollTimeMs value...
            if (timeSincePoll <= PollTimeMs)
            {
                // we keep incrementing our local counter until we do.
                timeSincePoll += Time.deltaTime;
                return;
            }

            // if we have a pending request still going, don't queue another yet.
            if (!lastGetComplete)
            {
                return;
            }

            // when we have reached our PollTimeMs value...
            timeSincePoll = 0f;

            // begin the poll and process.
            lastGetComplete = false;
            StartCoroutine(CO_GetAndProcessFromServer());
            
        }
    }
}
