# IniSharp
This is a C# Class that provides functionality for reading and writing INI  files. INI files are commonly used for configuration purposes in many applications.
Supports Windows,Linux,Android,iOS,macOS,Unity.

Features:

Reading and parsing INI files: The library allows you to easily read and parse INI files, extracting key-value pairs and sections.
Retrieving values: You can retrieve values from specific sections and keys in the INI file.
Modifying values: The library provides methods to modify existing values in the INI file.
Adding new sections and keys: You can add new sections and keys to the INI file.
Writing and saving changes: Once you have made modifications to the INI file, you can save the changes back to the file.
Usage:

Initialize the INIFile object with the path to the INI file.
Use the provided methods to read, modify, or add values.
Save the changes back to the INI file.

Example:
 ```C#
using IniFileSharp;
// Initialize the INIFile object
IniSharp iniFile = new IniSharp("config.ini");

// Read values from the INI file
string value = iniFile.GetValue("SectionName", "KeyName");

// Modify an existing value
iniFile.SetValue("SectionName", "KeyName", "NewValue");

// Add a new section and key
iniFile.SetValue("NewSection", "NewKey", "Value");
string value = iniFile.GetValue("SectionName", "KeyName","DefualtValue");

//Delete a Key
DeleteKey(section,key);

//Delete a Section
DeleteSection(section);

//Delete All Sections
DeleteAllSection();

// Get All sections
List<string> sections=GetSections();

// Get All Keys
List<string> Keys=GetKeys(section);
 ``` 

 
