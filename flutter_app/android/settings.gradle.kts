pluginManagement {
    val flutterSdkPath =
        run {
            val properties = java.util.Properties()
            file("local.properties").inputStream().use { properties.load(it) }
            val flutterSdkPath = properties.getProperty("flutter.sdk")
            require(flutterSdkPath != null) { "flutter.sdk not set in local.properties" }
            flutterSdkPath
        }

    includeBuild("$flutterSdkPath/packages/flutter_tools/gradle")

    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

plugins {
    id("dev.flutter.flutter-plugin-loader") version "1.0.0"
    id("com.android.application") version "9.0.1" apply false
    id("com.android.library") version "9.0.1" apply false
    id("org.jetbrains.kotlin.android") version "2.3.20" apply false
}

include(":app")

val unityExport = file("../unity_export")
require(file("$unityExport/unityLibrary/build.gradle").isFile) {
    "Unity library is missing. In Unity select Build > Export Unity Library for Flutter first."
}
include(":unityLibrary")
project(":unityLibrary").projectDir = file("$unityExport/unityLibrary")

val unityProperties = java.util.Properties()
file("$unityExport/gradle.properties").inputStream().use { unityProperties.load(it) }
gradle.beforeProject {
    unityProperties.stringPropertyNames().filter { it.startsWith("unity") }.forEach { name ->
        extensions.extraProperties.set(name, unityProperties.getProperty(name))
    }
}
