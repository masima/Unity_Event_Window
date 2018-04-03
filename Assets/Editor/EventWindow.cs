using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.Callbacks;


public class EventWindow : EditorWindow {

	[MenuItem("Window/EventWindow")]
	static void Open()
	{
		GetWindow(typeof(EventWindow));
	}

	static bool isTypeGeted;
	[DidReloadScripts]
	static void ScriptCompiled()
	{
		isTypeGeted = false;
	}


	Vector2 scrollPosition;
	Dictionary<Component, SerializedObject> serializedObjectDict = new Dictionary<Component, SerializedObject>();
	Dictionary<Component, SerializedObject> serializedObjectDictNext = new Dictionary<Component, SerializedObject>();

	bool eventExistOnly = false;
    bool activeOnly = false;

	private void OnGUI()
	{
		if (isTypeGeted == false) {
			isTypeGeted = true;
			GetTargetClassMembers();
		}


		// Tool bar

		GUI.skin.button.fixedHeight = 24;
		GUI.skin.button.fontSize = 18;
        float toolbarHeight = 16;

        var position = new Rect();
        position.width = 96;
        position.height = toolbarHeight;
		eventExistOnly = EditorGUI.Toggle(position, eventExistOnly);
		position.x += 16;
        position.width = 256;
		EditorGUI.LabelField(position, "Only Exist Event");
        position.x += 96;
        //position.width = 16;
        activeOnly = EditorGUI.Toggle(position, activeOnly);
        position.x += 16;
        //position.width = 256;
        EditorGUI.LabelField(position, "Only Active");


		// Contents

        position.x = 0;
		position.y = toolbarHeight;
        position.width = Screen.width / EditorGUIUtility.pixelsPerPoint;
        position.height = Screen.height / EditorGUIUtility.pixelsPerPoint - position.y - 22;

        GUILayout.BeginArea(position);
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        GameObject[] gameObjectAll;
        if (activeOnly) {
            gameObjectAll = (GameObject[])FindObjectsOfType(typeof(GameObject));
        }
        else {
            gameObjectAll = (GameObject[])Resources.FindObjectsOfTypeAll(typeof(GameObject));

        }
		foreach (GameObject gameObject in gameObjectAll)
		{
			var components = gameObject.GetComponents(typeof(Component));
			if (!components.Any(component => targetTypeDict.ContainsKey(component.GetType()))) {
				continue;
			}
			if (eventExistOnly) {
				if (!components.Any(component => IsEventExist(component))) {
					continue;
				}
			}

			if (GUILayout.Button(gameObject.name)) {
				Selection.activeGameObject = gameObject;
			}
			foreach (var component in components) {
				EventMember eventMember;
				if (targetTypeDict.TryGetValue(component.GetType(), out eventMember)) {
					SerializedObject so;
					if (!serializedObjectDict.TryGetValue(component, out so)) {
						so = new SerializedObject(component);
					}
					so.Update();
					serializedObjectDictNext[component] = so;

					EditorGUILayout.LabelField(component.GetType().Name);
					foreach (var member in eventMember.members) {
						var prop = so.FindProperty(member);
						if (prop != null) {
							EditorGUILayout.PropertyField(prop, includeChildren: true);
						}
						else {
							EditorGUILayout.LabelField("not find class:" + component.GetType() + " property:" + member);
						}
					}
					so.ApplyModifiedProperties();
				}
			}
		}
		EditorGUILayout.EndScrollView();
        GUILayout.EndArea();


		SwapSerializedObjectDict();

	}

	bool IsEventExist(Component component)
	{
		EventMember eventMember;
		if (targetTypeDict.TryGetValue(component.GetType(), out eventMember)) {
			if (IsEventExist(component, eventMember)) {
				return true;
			}
		}
		return false;
	}

	bool IsEventExist(Component component, EventMember eventMember) {
		var type = component.GetType();
		foreach (var member in eventMember.members) {
			var members = type.GetMember(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (members == null || members.Length == 0) {
				EditorGUILayout.LabelField("not found member:" + member);
			}
			else {
				var m = members[0];
				object obj = null;
				if (m.MemberType == MemberTypes.Field) {
					var info = (FieldInfo)m;
					obj = info.GetValue(component);
				}
				else if (m.MemberType == MemberTypes.Property) {
					var info = (PropertyInfo)m;
					obj = info.GetValue(component, null);
				}
				if (obj != null) {
					if (obj.GetType().IsSubclassOf(typeof(UnityEventBase))) {
						var eventBase = (UnityEventBase)obj;
						if (0 < eventBase.GetPersistentEventCount()) {
							return true;
						}
					}
					else if (obj.GetType() == typeof(List<EventTrigger.Entry>)) {
						var list = (List<EventTrigger.Entry>)obj;
						foreach (var entry in list) {
							if (0 < entry.callback.GetPersistentEventCount()) {
								return true;
							}
						}
					}
				}
			}
		}
		return false;
	}

	void SwapSerializedObjectDict()
	{
		var temp = serializedObjectDict;
		serializedObjectDict = serializedObjectDictNext;
		serializedObjectDictNext = temp;
		serializedObjectDictNext.Clear();
	}


	class EventMember {
		public System.Type type;
		public List<string> members = new List<string>();
	}
	Dictionary<Type, EventMember> targetTypeDict = new Dictionary<Type, EventMember>();

	void GetTargetClassMembers()
	{
		targetTypeDict.Clear();
		foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
			foreach (var type in asm.GetTypes()) {
				if (!type.IsSubclassOf(typeof(Component))) {
					continue;
				}
				foreach (var info in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
					if (IsObsolete(info)) {
						continue;
					}
					if (IsTargetType(info.FieldType)) {
						EntryClassMember(type, info);
					}
				}
				foreach (var info in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)) {
					if (IsObsolete(info)) {
						continue;
					}
					if (!IsSerializeField(info)) {
						continue;
					}
					if (IsTargetType(info.FieldType)) {
						EntryClassMember(type, info);
					}
				}
				foreach (var info in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
					if (IsObsolete(info)) {
						continue;
					}
					if (!IsSerializeField(info)) {
						continue;
					}
					if (IsTargetType(info.PropertyType)) {
						EntryClassMember(type, info);
					}
				}
			}
		}
	}
	void EntryClassMember(Type type, MemberInfo m)
	{
		// var attrString = string.Join(" ", Attribute.GetCustomAttributes(m).Select(_ => _.GetType().ToString()).ToArray());
		// var mes = "type:" + type + " name:" + m.Name + " attribute:" + attrString;
		// Debug.Log(mes);

		EventMember eventMember;
		if (!targetTypeDict.TryGetValue(type, out eventMember)) {
			eventMember = new EventMember { type = type };
			targetTypeDict[type] = eventMember;
		}
		eventMember.members.Add(m.Name);
	}


	static bool IsTargetType(Type type)
	{
		if (type.IsSubclassOf(typeof(UnityEventBase))
		 || type == typeof(List<EventTrigger.Entry>)
		) {
			return true;
		}
		return false;

	}
	static bool IsObsolete(MemberInfo memberInfo)
	{
		return memberInfo.IsDefined(typeof(ObsoleteAttribute), inherit: true);
	}
	static bool IsSerializeField(MemberInfo memberInfo) {
		return memberInfo.IsDefined(typeof(SerializeField), inherit: true);
	}

}
