﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GitCommander
{
	[Flags]
	public enum FileStates
	{
		Unaltered = 0,
		ModifiedInWorkdir = 1,
		ModifiedInIndex = 2,
		NewInWorkdir = 4,
		NewInIndex = 8,
		DeletedFromWorkdir = 16,
		DeletedFromIndex = 32,
		RenamedInWorkdir = 64,
		RenamedInIndex = 128,
		TypeChangeInWorkdir = 256,
		TypeChangeInIndex = 512,
		Conflicted = 1024,
		Ignored = 2048,
		Unreadable = 4096,
		Copied = 8192
	}

	public enum FileConflictSources
	{
		Ours,
		Theirs
	}

	public enum FileConflictTypes
	{
		None,
		Changes,
		DeletedByUs,
		DeletedByThem,
		DeletedByBoth
	}

	public class FileState
	{
		public string filename {get; internal set;}
		public FileStates state {get; internal set;}
		public FileConflictTypes conflictType {get; internal set;}

		public static bool IsAllStates(FileStates stateFlag, FileStates[] states)
		{
			foreach (var state in states)
			{
				if ((stateFlag & state) == 0) return false;
			}

			return true;
		}

		public static bool IsAnyStates(FileStates stateFlag, FileStates[] states)
		{
			foreach (var state in states)
			{
				if ((stateFlag & state) != 0) return true;
			}

			return false;
		}

		public bool IsAllStates(FileStates[] states)
		{
			return IsAllStates(state, states);
		}

		public bool IsAnyStates(FileStates[] states)
		{
			return IsAnyStates(state, states);
		}

		public bool HasState(FileStates state)
		{
			return (this.state & state) != 0;
		}

		public bool IsUnstaged()
		{
			return
				HasState(FileStates.NewInWorkdir) ||
				HasState(FileStates.DeletedFromWorkdir) ||
				HasState(FileStates.ModifiedInWorkdir) ||
				HasState(FileStates.RenamedInWorkdir) ||
				HasState(FileStates.TypeChangeInWorkdir) ||
				HasState(FileStates.Conflicted);
		}

		public bool IsStaged()
		{
			return
				HasState(FileStates.NewInIndex) ||
				HasState(FileStates.DeletedFromIndex) ||
				HasState(FileStates.ModifiedInIndex) ||
				HasState(FileStates.RenamedInIndex) ||
				HasState(FileStates.TypeChangeInIndex);
		}

		public override string ToString()
		{
			return filename;
		}
	}

	public partial class Repository
	{
		public bool Stage(string filename)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("add \"{0}\"", filename));
			}
		}

		public bool StageAll()
		{
			lock (this)
			{
				return SimpleGitInvoke("add -A");
			}
		}

		public bool Unstage(string filename)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("reset \"{0}\"", filename));
			}
		}

		public bool UnstageAll()
		{
			lock (this)
			{
				return SimpleGitInvoke("reset");
			}
		}

		public bool RevertFile(string activeBranch, string filename)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("checkout {0} -- \"{1}\"", activeBranch, filename));
			}
		}

		public bool RevertAllChanges()
		{
			lock (this)
			{
				return SimpleGitInvoke("reset --hard");
			}
		}
		
		private bool ParseFileState(string line, ref int mode, List<FileState> states)
		{
			bool addState(string type, FileStates stateType, FileConflictTypes conflictType = FileConflictTypes.None)
			{
				if (line.Contains(type))
				{
					var match = Regex.Match(line, type + @"\s*(.*)");
					if (match.Groups.Count == 2)
					{
						string filePath = match.Groups[1].Value;
						if ((stateType & FileStates.Copied) != 0)
						{
							match = Regex.Match(filePath, @"(.*)\s->\s(.*)");
							if (match.Success) filePath = match.Groups[2].Value;
							else throw new Exception("Failed to parse copied status type");
						}
						
						if (states != null && states.Exists(x => x.filename == filePath))
						{
							var state = states.Find(x => x.filename == filePath);
							state.state |= stateType;
							return true;
						}
						else
						{
							var state = new FileState()
							{
								filename = filePath,
								state = stateType,
								conflictType = conflictType
							};
							
							states.Add(state);
							return true;
						}
					}
				}
				
				return false;
			}

			lock (this)
			{
				// gather normal files
				switch (line)
				{
					case "Changes to be committed:": mode = 0; return true;
					case "Changes not staged for commit:": mode = 1; return true;
					case "Unmerged paths:": mode = 2; return true;
					case "Untracked files:": mode = 3; return true;
				}
			
				bool pass = false;
				if (mode == 0)
				{
					pass = addState("\tnew file:", FileStates.NewInIndex);
					if (!pass) pass = addState("\tmodified:", FileStates.ModifiedInIndex);
					if (!pass) pass = addState("\tdeleted:", FileStates.DeletedFromIndex);
					if (!pass) pass = addState("\trenamed:", FileStates.RenamedInIndex);
					if (!pass) pass = addState("\tcopied:", FileStates.Copied | FileStates.NewInIndex);
				}
				else if (mode == 1)
				{
					pass = addState("\tmodified:", FileStates.ModifiedInWorkdir);
					if (!pass) pass = addState("\tdeleted:", FileStates.DeletedFromWorkdir);
					if (!pass) pass = addState("\trenamed:", FileStates.RenamedInWorkdir);
					if (!pass) pass = addState("\tcopied:", FileStates.Copied | FileStates.NewInWorkdir);
					if (!pass) pass = addState("\tnew file:", FileStates.NewInWorkdir);// call this just in case (should be done in untracked)
				}
				else if (mode == 2)
				{
					pass = addState("\tboth modified:", FileStates.Conflicted, FileConflictTypes.Changes);
					if (!pass) pass = addState("\tdeleted by us:", FileStates.Conflicted, FileConflictTypes.DeletedByUs);
					if (!pass) pass = addState("\tdeleted by them:", FileStates.Conflicted, FileConflictTypes.DeletedByThem);
					if (!pass) pass = addState("\tboth deleted:", FileStates.Conflicted, FileConflictTypes.DeletedByBoth);
				}
				else if (mode == 3)
				{
					pass = addState("\t", FileStates.NewInWorkdir);
				}

				if (!pass)
				{
					var match = Regex.Match(line, @"\t(.*):");
					if (match.Success) return false;
				}

				return true;
			}
		}

		public bool GetFileState(string filename, out FileState fileState)
		{
			var states = new List<FileState>();
			int mode = -1;
			bool failedToParse = false;
			void stdCallback(string line)
			{
				if (!ParseFileState(line, ref mode, states)) failedToParse = true;
			}

			lock (this)
			{
				var result = RunExe("git", string.Format("status -u \"{0}\"", filename), stdCallback:stdCallback);
				lastResult = result.output;
				lastError = result.errors;
				if (!string.IsNullOrEmpty(lastError))
				{
					fileState = null;
					return false;
				}

				if (failedToParse)
				{
					fileState = null;
					return false;
				}
			
				if (states.Count != 0)
				{
					fileState = states[0];
					return true;
				}
				else
				{
					fileState = null;
					return false;
				}
			}
		}

		public bool GetFileStates(out FileState[] fileStates)
		{
			var states = new List<FileState>();
			int mode = -1;
			bool failedToParse = false;
			void stdCallback(string line)
			{
				if (!ParseFileState(line, ref mode, states))failedToParse = true;
			}

			lock (this)
			{
				var result = RunExe("git", "status -u", stdCallback:stdCallback);
				lastResult = result.output;
				lastError = result.errors;
				if (!string.IsNullOrEmpty(lastError))
				{
					fileStates = null;
					return false;
				}

				if (failedToParse)
				{
					fileStates = null;
					return false;
				}

				fileStates = states.ToArray();
				return true;
			}
		}

		public bool ConflitedExist(out bool yes)
		{
			bool conflictExist = false;
			void stdCallback(string line)
			{
				conflictExist = true;
			}

			lock (this)
			{
				var result = RunExe("git", "diff --name-only --diff-filter=U", null, stdCallback:stdCallback);
				lastResult = result.output;
				lastError = result.errors;
			
				yes = conflictExist;
				return string.IsNullOrEmpty(lastError);
			}
		}

		public bool SaveOriginalFile(string filename, out string savedFilename)
		{
			lock (this)
			{
				savedFilename = filename + ".orig";
				var result = RunExe("git", string.Format("show HEAD:\"{0}\"", filename), stdOutToFilePath:savedFilename);
				lastResult = result.output;
				lastError = result.errors;

				return string.IsNullOrEmpty(lastError);
			}
		}

		public bool SaveConflictedFile(string filename, FileConflictSources source, out string savedFilename)
		{
			lock (this)
			{
				string sourceName = source == FileConflictSources.Ours ? "ORIG_HEAD" : "MERGE_HEAD";
				savedFilename = filename + (source == FileConflictSources.Ours ? ".ours" : ".theirs");
				var result = RunExe("git", string.Format("show {1}:\"{0}\"", filename, sourceName), stdOutToFilePath:savedFilename);
				lastResult = result.output;
				lastError = result.errors;

				return string.IsNullOrEmpty(lastError);
			}
		}

		public bool CheckoutConflictedFile(string filename, FileConflictSources source)
		{
			lock (this)
			{
				string sourceName = source == FileConflictSources.Ours ? "--ours" : "--theirs";
				return SimpleGitInvoke(string.Format("checkout {1} \"{0}\"", filename, sourceName));
			}
		}

		public bool RemoveFile(string filename)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("rm \"{0}\"", filename));
			}
		}

		public bool CompletedMergeCommitPending(out bool yes)
		{
			bool mergeCommitPending = false;
			void stdCallback(string line)
			{
				if (line == "All conflicts fixed but you are still merging.") mergeCommitPending = true;
			}

			lock (this)
			{
				var result = RunExe("git", "status", null, stdCallback:stdCallback);
				lastResult = result.output;
				lastError = result.errors;
			
				yes = mergeCommitPending;
				return string.IsNullOrEmpty(lastError);
			}
		}

		public bool Fetch()
		{
			lock (this)
			{
				return SimpleGitInvoke("fetch");
			}
		}

		public bool Fetch(string remote, string branch)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("fetch {0} {1}", remote, branch));
			}
		}

		public bool Pull()
		{
			lock (this)
			{
				return SimpleGitInvoke("pull");
			}
		}

		public bool Push()
		{
			lock (this)
			{
				return SimpleGitInvoke("push");
			}
		}
		
		public bool Commit(string message)
		{
			lock (this)
			{
				return SimpleGitInvoke(string.Format("commit -m \"{0}\"", message));
			}
		}

		public bool GetDiff(string filename, out string diff)
		{
			lock (this)
			{
				bool result = SimpleGitInvoke(string.Format("diff HEAD \"{0}\"", filename));
				diff = lastResult;
				return result;
			}
		}
	}
}
