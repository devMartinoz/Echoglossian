// <copyright file="TtsDialogueBridge.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.NativeUI.AddonHandlers.Common;

/// <summary>
///     Exposes the latest accepted dialogue translation to local external TTS tools.
/// </summary>
internal static class TtsDialogueBridge
{
  private static readonly object StateGate = new();
  private static string? lastRequestedText;
  private static string? pendingText;
  private static bool writerRunning;

  /// <summary>
  ///     Schedules a UTF-8 text-only publication without doing file I/O on the caller.
  ///     Consecutive duplicates, including failed writes, are ignored to avoid repaint retries.
  /// </summary>
  /// <param name="translatedText">The accepted translated body, without the speaker name.</param>
  internal static void Publish(string? translatedText)
  {
    if (string.IsNullOrWhiteSpace(translatedText))
    {
      return;
    }

    // This optional local external TTS bridge must never affect plugin behavior.
    try
    {
      lock (StateGate)
      {
        if (string.Equals(lastRequestedText, translatedText, StringComparison.Ordinal))
        {
          return;
        }

        lastRequestedText = translatedText;
        pendingText = translatedText;
        if (!writerRunning)
        {
          writerRunning = ThreadPool.QueueUserWorkItem(_ => WritePendingText());
        }
      }
    }
    catch (Exception)
    {
      // Scheduling failure must also remain isolated from the dialogue handlers.
    }
  }

  /// <summary>
  ///     Serializes file writes and coalesces pending publications to the latest line.
  /// </summary>
  private static void WritePendingText()
  {
    while (true)
    {
      string? text;
      lock (StateGate)
      {
        text = pendingText;
        pendingText = null;
        if (text == null)
        {
          writerRunning = false;
          return;
        }
      }

      WriteText(text);
    }
  }

  /// <summary>
  ///     Writes a complete sibling file before replacing the public bridge file.
  ///     All file failures are ignored; the previous line is retained when possible.
  /// </summary>
  /// <param name="text">The exact accepted translation to write.</param>
  private static void WriteText(string text)
  {
    string? temporaryPath = null;
    try
    {
      var path = Path.Combine(Path.GetTempPath(), "echoglossian_current_dialogue.txt");
      temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
      File.Move(temporaryPath, path, overwrite: true);
    }
    catch (Exception)
    {
      // Locked files, denied access, or unavailable storage must never break dialogue.
    }
    finally
    {
      if (temporaryPath != null)
      {
        try
        {
          File.Delete(temporaryPath);
        }
        catch (Exception)
        {
          // Cleanup is best effort, too.
        }
      }
    }
  }
}
