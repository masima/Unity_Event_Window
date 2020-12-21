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
	readonly List<GameObject> targetGameObjects = new List<GameObject>();

	bool activeOnly = false;
	bool showFullPath = true;

	enum PickupEventType
	{
		All,
		EventExist,
		Missing,
	}
	PickupEventType pickupEvent = PickupEventType.All;


	private void OnGUI()
	{
		if (isTypeGeted == false) {
			isTypeGeted = true;
			GetTargetClassMembers();
			ListupTargetGameObjects();
		}


		// Tool bar

		GUI.skin.button.fixedHeight = 16;
		GUI.skin.button.fontSize = 12;
		var position = new Rect();
		float toolbarHeight = 20;

		EditorGUI.BeginChangeCheck();
		{
			{
				position.width = 96;
				position.height = toolbarHeight;
				EditorGUI.LabelField(position, "Event");
				position.x += 48;
				position.width = 96;
				pickupEvent = (PickupEventType)EditorGUI.EnumPopup(position, pickupEvent);

			}
			{
				position.x += 96 + 8;
				position.width = 96;
				activeOnly = EditorGUI.Toggle(position, activeOnly);
				position.x += 16;
				position.width = 96;
				EditorGUI.LabelField(position, "Only Active");
			}
			{
				position.x += 96;
				position.width = 96;
				showFullPath = EditorGUI.Toggle(position, showFullPath);
				position.x += 16;
				position.width = 96;
				EditorGUI.LabelField(position, "Show Full Path");
			}
			{
				var width = 64;
				var height = 24;
				position.x = Screen.width - width;
				position.width = width;
				position.height = height;
				GUI.Button(position, "Update");
			}
		}
		if (EditorGUI.EndChangeCheck())
		{
			ListupTargetGameObjects();
		}


		// Contents

		GUI.skin.button.fixedHeight = 20;
		GUI.skin.button.fontSize = 18;

		position.x = 0;
		position.y = toolbarHeight;
		position.width = Screen.width / EditorGUIUtility.pixelsPerPoint;
		position.height = Screen.height / EditorGUIUtility.pixelsPerPoint - position.y - 22;

		GUILayout.BeginArea(position);
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

		foreach (var gameObject in targetGameObjects)
		{
			if (null == gameObject)
			{
				continue;
			}

			GUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("選択"))
				{
					Selection.activeGameObject = gameObject;
				}
				GUILayout.Label(GetObjectName(gameObject));
				GUILayout.FlexibleSpace();
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginVertical("box");
			{
				var components = gameObject.GetComponents(typeof(Component));
				foreach (var component in components)
				{
					EventMember eventMember;
					if (targetTypeDict.TryGetValue(component.GetType(), out eventMember))
					{
						SerializedObject so;
						if (!serializedObjectDict.TryGetValue(component, out so))
						{
							so = new SerializedObject(component);
						}
						so.Update();
						serializedObjectDictNext[component] = so;

						EditorGUILayout.LabelField(component.GetType().Name);
						foreach (var member in eventMember.members)
						{
							var prop = so.FindProperty(member);
							if (prop != null)
							{
								EditorGUILayout.PropertyField(prop, includeChildren: true);
							}
							else
							{
								EditorGUILayout.LabelField("not find class:" + component.GetType() + " property:" + member);
							}
						}
						so.ApplyModifiedProperties();
					}
				}
			}
			GUILayout.EndVertical();
		}
		EditorGUILayout.EndScrollView();
		GUILayout.EndArea();


		SwapSerializedObjectDict();

	}

	/// <summary>
	/// ターゲットとなるGameObjectリストアップ
	/// </summary>
	void ListupTargetGameObjects()
	{
		targetGameObjects.Clear();

		GameObject[] gameObjectAll;
		if (activeOnly)
		{
			gameObjectAll = (GameObject[])FindObjectsOfType(typeof(GameObject));
		}
		else
		{
			gameObjectAll = (GameObject[])Resources.FindObjectsOfTypeAll(typeof(GameObject));

		}
		foreach (GameObject gameObject in gameObjectAll)
		{
			var components = gameObject.GetComponents(typeof(Component));
			if (null == components || !components.Any(component => (null != component && null != targetTypeDict && targetTypeDict.ContainsKey(component.GetType()))))
			{
				continue;
			}
			if (pickupEvent != PickupEventType.All)
			{
				if (!components.Any(component => IsEventExist(component)))
				{
					continue;
				}
			}
			targetGameObjects.Add(gameObject);
		}

	}

	/// <summary>
	/// GameObjectの表示名称取得
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	string GetObjectName(GameObject obj)
	{
		if (showFullPath)
		{
			return GetFullPath(obj);
		}
		return obj.name;
	}
	string GetFullPath(GameObject obj)
	{
		var stack = new Stack<string>();
		var current = obj.transform;
		while (null != current)
		{
			stack.Push(current.name);
			current = current.parent;
		}
		var path = string.Join("/", stack.ToArray());
		return path;
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
				//EditorGUILayout.LabelField("not found member:" + member);
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
							return IsEventExist(eventBase);
						}
					}
					else if (obj.GetType() == typeof(List<EventTrigger.Entry>)) {
						var list = (List<EventTrigger.Entry>)obj;
						foreach (var entry in list) {
							if (0 < entry.callback.GetPersistentEventCount()) {
								return IsEventExist(entry.callback);
							}
						}
					}
				}
			}
		}
		return false;
	}

	bool IsEventExist(UnityEventBase unityEvent)
	{
		for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
		{
			var target = unityEvent.GetPersistentTarget(i);
			if (pickupEvent == PickupEventType.Missing)
			{
				if (IsMissing(target))
				{
					return true;
				}
			}
			else if (pickupEvent == PickupEventType.EventExist)
			{
				if (null != target)
				{
					return true;
				}
			}
		}
		return false;
	}

	bool IsMissing(UnityEngine.Object obj)
	{
		try
		{
			var name = obj.name;
		}
		catch (MissingReferenceException)
		{
			return true;
		}
		catch
		{
			return false;
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
