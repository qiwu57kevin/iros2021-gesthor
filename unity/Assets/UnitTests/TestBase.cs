
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using System;
using UnityEngine;
using UnityEngine.TestTools;
using UnityStandardAssets.Characters.FirstPerson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Tests {
    public class TestBase {
        protected int sequenceId = 0;
        protected bool lastActionSuccess;
        protected string error;


        public IEnumerator step(Dictionary<string, object> action) {
            var agentManager = GameObject.FindObjectOfType<AgentManager>();
            action["sequenceId"] = sequenceId;
            agentManager.ProcessControlCommand(new DynamicServerAction(action));
            yield return new WaitForEndOfFrame();
            var agent = agentManager.GetActiveAgent();
            lastActionSuccess = agent.lastActionSuccess;
            error = agent.errorMessage;
            sequenceId++;
        }

        public IEnumerator initalizeDefaultDiscrete() {
            Dictionary<string, object> action = new Dictionary<string, object>() {
                { "gridSize", 0.25f},
                { "agentCount", 1},
                { "fieldOfView", 90f},
                { "snapToGrid", true},
                { "action", "Initialize"}
            };
            yield return step(action);
        }

        [SetUp]
        public virtual void Setup() {
            UnityEngine.SceneManagement.SceneManager.LoadScene("FloorPlan1_physics");
        }

    }
}
