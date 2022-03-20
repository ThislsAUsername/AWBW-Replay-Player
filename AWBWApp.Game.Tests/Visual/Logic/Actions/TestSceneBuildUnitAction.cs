﻿using AWBWApp.Game.API.Replay.Actions;
using NUnit.Framework;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.Tests.Visual.Logic.Actions
{
    [TestFixture]
    public class TestSceneBuildUnitAction : BaseActionsTestScene
    {
        private static Vector2I unitPosition = new Vector2I(2, 2);

        [Test]
        public void TestCreateUnit()
        {
            AddStep("Setup", createTest);
            AddStep("Create Unit", ReplayController.GoToNextAction);
            AddAssert("Unit was created", () => ReplayController.Map.TryGetDrawableUnit(0, out _));
            AddAssert("Building is done", () => ReplayController.Map.TryGetDrawableBuilding(unitPosition, out var building) && building.HasDoneAction.Value);
            AddStep("Undo", ReplayController.UndoAction);
            AddAssert("Unit doesn't exist", () => !ReplayController.Map.TryGetDrawableUnit(0, out _));
            AddAssert("Building is not done", () => ReplayController.Map.TryGetDrawableBuilding(unitPosition, out var building) && !building.HasDoneAction.Value);
        }

        private void createTest()
        {
            var replayData = CreateBasicReplayData(2);

            var turn = CreateBasicTurnData(replayData);
            replayData.TurnData.Add(turn);

            var building = CreateBasicReplayBuilding(0, unitPosition, 39);
            turn.Buildings.Add(building.Position, building);

            var createdUnit = CreateBasicReplayUnit(0, 1, "Infantry", unitPosition);

            var createUnitAction = new BuildUnitAction
            {
                NewUnit = createdUnit
            };
            turn.Actions.Add(createUnitAction);

            ReplayController.LoadReplay(replayData, CreateBasicMap(5, 5));
            ReplayController.AllowRewinding = true;
        }
    }
}
