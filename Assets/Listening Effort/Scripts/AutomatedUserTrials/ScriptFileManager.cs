using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ScriptFileManager : MonoBehaviour
{
    public string scriptsDirectory => $"{Application.persistentDataPath}/Scripts";
    public TextAsset demoScript;

    private string[] _scripts = null;
    public string[] scripts
    {
        get
        {
            if (_scripts == null)
            {
                VideoCatalogue videoCatalogue = FindObjectOfType<VideoCatalogue>();
                Debug.Assert(videoCatalogue != null);

                if (!Directory.Exists(scriptsDirectory))
                {
                    Debug.Log("Scripts directory does not exist. Will create and copy in the demo script.");
                    Directory.CreateDirectory(scriptsDirectory);
                    File.WriteAllText($"{scriptsDirectory}/demo.yaml", demoScript.text);
                }

                List<string> validatedScripts = new List<string>();
                foreach (string path in Directory.GetFiles(scriptsDirectory))
                {
                    if (path.EndsWith(".yaml") || path.EndsWith(".yml"))
                    {
                        try
                        {
                            Session session = Session.LoadFromYaml(path, videoCatalogue);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Error reading session at {path}\n{e}", this);
                        }
                        validatedScripts.Add(path);
                    }
                }
                _scripts = validatedScripts.ToArray();
            }
            return _scripts;
        }
    }
}
