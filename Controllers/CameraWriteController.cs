﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using EchoVRAPI;
using static Logger;

namespace Spark
{
	class CameraWriteController
	{
		private static string BaseUrl => "http://127.0.0.1:" + (Program.spectateMeController.spectateMe ? SpectateMeController.SPECTATEME_PORT : "6721") + "/";

		public CameraWriteController()
		{
			Program.Goal += (_, _) =>
			{
				if (SparkSettings.instance.toggleMinimapAfterGoals)
				{
					Task.Run(async () =>
					{
						await Task.Delay(500);

						SetMinimapVisibility(false);
						await Task.Delay(20);
						SetMinimapVisibility(true);
					});
				}
			};

			Program.JoinedGame += (_) => { UseCameraControlKeys(); };
			Program.PlayerJoined += (_, _, _) => { UseCameraControlKeys(); };
			Program.PlayerLeft += (_, _, _) => { UseCameraControlKeys(); };
			Program.PlayerSwitchedTeams += (_, _, _, _) => { UseCameraControlKeys(); };
		}

		public static void UseCameraControlKeys()
		{
			try
			{
				SetNameplatesVisibility(!SparkSettings.instance.hideNameplates);
				SetUIVisibility(!SparkSettings.instance.hideEchoVRUI);
				SetMinimapVisibility(!SparkSettings.instance.alwaysHideMinimap);
				SetPlayersMuted();

				switch (SparkSettings.instance.spectatorCamera)
				{
					// auto
					case 0:
						break;
					// sideline
					case 1:
						SetCameraMode(CameraMode.side);
						break;
					// follow client
					case 2:
						if (Program.spectateMeController.spectateMe) SpectatorCamFindPlayer();
						break;
					// follow specific player
					case 3:
						SpectatorCamFindPlayer(SparkSettings.instance.followPlayerName);
						break;
				}
			}
			catch (Exception ex)
			{
				LogRow(LogType.Error, $"Error with in setting cameras after started spectating\n{ex}");
			}
		}

		public static void SpectatorCamFindPlayer(string playerName = null)
		{
			if (Program.lastFrame == null) return;

			playerName ??= Program.lastFrame.client_name;

			if (Program.lastFrame.GetPlayer(playerName) == null)
			{
				LogRow(LogType.File, Program.lastFrame.sessionid, "Requested follow player not in the game.");
				return;
			}

			try
			{
				Task.Run(async () =>
				{
					await Task.Delay(500);

					// try to find player
					SetCameraMode(CameraMode.pov);

					await Task.Delay(50);
					bool found;
					found = await CheckPovCamIsCorrect(playerName);
					int foundIndex = 0;

					// loop through all the players twice if we don't find the right one the first time
					int foundTries = 1;
					while (foundTries > 0 && !found)
					{
						for (int i = 0; i < Keyboard.numbers.Length; i++)
						{
							// press the keys to visit a player
							SetCameraMode(CameraMode.pov, i);

							// check if this is the right player
							await Task.Delay(50);
							found = await CheckPovCamIsCorrect(playerName, i);

							// if we found the correct player
							if (found)
							{
								foundIndex = i;
								break;
							}
						}

						foundTries--;
					}

					if (found)
					{
						LogRow(LogType.File, Program.lastFrame.sessionid, "Correct player found.");
						switch (SparkSettings.instance.followPlayerCameraMode)
						{
							// Follow
							case 0:
								SetCameraMode(CameraMode.follow, foundIndex);
								break;
							// POV
							case 1:
								SetCameraMode(CameraMode.pov, foundIndex);
								break;
						}
					}
					else
					{
						LogRow(LogType.File, Program.lastFrame.sessionid,
							"Failed to find player, switching to auto instead.");

						SetCameraMode(CameraMode.side);
					}
				});
			}
			catch (Exception ex)
			{
				LogRow(LogType.Error, $"Error with finding player camera to follow.\n{ex}");
			}
		}

		private static async Task<bool> CheckPovCamIsCorrect(string playerName, int i = -1)
		{
			string result = await Program.GetRequestAsync(BaseUrl + "session", null);
			if (string.IsNullOrEmpty(result)) return false;
			Frame frame = JsonConvert.DeserializeObject<Frame>(result);
			if (frame == null) return false;
			List<Player> players = frame.GetAllPlayers();

			List<Player> sortedList = players
				.OrderBy(p => Vector3.Distance(p.head.Position, frame.player.vr_position.ToVector3())).ToList();

			// debug all player distances
			//sortedList.ForEach(p => LogRow(LogType.File, frame.sessionid, $"{Vector3.Distance(p.head.Position, frame.player.vr_position.ToVector3())}\t{p.name}"));

			Player minPlayer = sortedList.First();
			float dist = Vector3.Distance(minPlayer.head.Position, frame.player.vr_position.ToVector3());

			LogRow(LogType.File, frame.sessionid, $"Player {i} camera distance: {dist:N3} m.  Name: {minPlayer.name}");

			return minPlayer.name == playerName;
		}

		public static void SetPlayersMuted()
		{
			if (SparkSettings.instance.mutePlayerComms)
			{
				SetTeamsMuted(true, true);
			}
			else if (SparkSettings.instance.muteEnemyTeam)
			{
				if (Program.connectionState == Program.ConnectionState.InGame && Program.lastFrame != null)
				{
					switch (Program.lastFrame.ClientTeamColor)
					{
						case Team.TeamColor.blue:
							SetTeamsMuted(false, true);
							break;
						case Team.TeamColor.orange:
							SetTeamsMuted(true, false);
							break;
						case Team.TeamColor.spectator:
							SetTeamsMuted(true, true);
							break;
					}
				}
			}
		}

		public static void SetUIVisibility(bool visible)
		{
			Dictionary<string, bool> data = new Dictionary<string, bool>()
			{
				{ "enabled", visible }
			};
			Program.PostRequestCallback(BaseUrl + "ui_visibility", null, JsonConvert.SerializeObject(data), null);
		}

		public static void SetNameplatesVisibility(bool visible)
		{
			Dictionary<string, object> data = new Dictionary<string, object>()
			{
				{ "enabled", visible }
			};
			Program.PostRequestCallback(BaseUrl + "nameplates_visibility", null, JsonConvert.SerializeObject(data), null);
		}

		public static void SetMinimapVisibility(bool visible)
		{
			Dictionary<string, object> data = new Dictionary<string, object>()
			{
				{ "enabled", visible }
			};
			Program.PostRequestCallback(BaseUrl + "minimap_visibility", null, JsonConvert.SerializeObject(data), null);
		}

		public static void SetTeamsMuted(bool blueTeamMuted, bool orangeTeamMuted)
		{
			Dictionary<string, bool> data = new Dictionary<string, bool>()
			{
				{ "blue_team_muted", blueTeamMuted },
				{ "orange_team_muted", orangeTeamMuted },
			};
			Program.PostRequestCallback(BaseUrl + "team_muted", null, JsonConvert.SerializeObject(data), null);
		}

		public enum CameraMode
		{
			pov,
			level,
			follow,
			side,
			free,
			api
		}

		public static void SetCameraMode(CameraMode mode)
		{
			Dictionary<string, object> data = new Dictionary<string, object>()
			{
				{ "mode", mode.ToString() },
			};
			Program.PostRequestCallback(BaseUrl + "camera_mode", null, JsonConvert.SerializeObject(data), null);
		}

		public static void SetCameraMode(int playerId)
		{
			Dictionary<string, object> data = new Dictionary<string, object>()
			{
				{ "num", playerId },
			};
			Program.PostRequestCallback(BaseUrl + "camera_mode", null, JsonConvert.SerializeObject(data), null);
		}

		public static void SetCameraMode(CameraMode mode, int playerId)
		{
			Dictionary<string, object> data = new Dictionary<string, object>()
			{
				{ "mode", mode.ToString() },
				{ "num", playerId },
			};
			Program.PostRequestCallback(BaseUrl + "camera_mode", null, JsonConvert.SerializeObject(data), null);
		}


		public static void SetCameraTransform(CameraTransform data)
		{
			Program.PostRequestCallback(BaseUrl + "camera_transform", null, JsonConvert.SerializeObject(data), null);
		}
	}
}