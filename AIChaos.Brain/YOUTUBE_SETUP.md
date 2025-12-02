# 📺 YouTube Integration Setup Guide

This guide will help you connect AI Chaos to your YouTube Live Chat so viewers can **support you with Super Chats that add credits to their account**.

> **Note**: Super Chats add credits to user accounts. Users then use those credits on the web interface to send commands to your game.

## 📋 Prerequisites
- A Google Account
- A YouTube Channel (for testing live chat)

## 🚀 Step 1: Create a Google Cloud Project
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Click the project dropdown at the top left (next to the Google Cloud logo).
3. Click **New Project**.
4. Name it `AI Chaos` (or anything you like) and click **Create**.
5. Select your new project from the notification or the dropdown.

## 🔌 Step 2: Enable YouTube Data API
1. In the left sidebar, go to **APIs & Services** > **Library**.
2. Search for `YouTube Data API v3`.
3. Click on it and click **Enable**.

## 🔐 Step 3: Configure OAuth Consent Screen
1. In the left sidebar, go to **APIs & Services** > **OAuth consent screen**.
2. Select **External** and click **Create**.
3. Fill in the required fields:
   - **App Name**: `AI Chaos`
   - **User Support Email**: Your email
   - **Developer Contact Information**: Your email
4. Click **Save and Continue**.
5. Skip "Scopes" (just click **Save and Continue**).
6. **IMPORTANT**: Under **Test Users**, click **Add Users** and enter your own Google email address.
   - *Note: Since the app is in "Testing" mode, only users listed here can log in.*
7. Click **Save and Continue**, then **Back to Dashboard**.

## 🔑 Step 4: Create Credentials
1. In the left sidebar, go to **APIs & Services** > **Credentials**.
2. Click **+ CREATE CREDENTIALS** > **OAuth client ID**.
3. **Application type**: Select **Web application**.
4. **Name**: `AI Chaos Local`
5. **Authorized JavaScript origins**:
   - Click **ADD URI**.
   - Enter: `http://localhost:5000` (or `http://127.0.0.1:5000`)
   - *If you are using a tunnel (like ngrok), also add that URL (e.g., `https://your-tunnel.ngrok.io`)*
6. **Authorized redirect URIs**:
   - Click **ADD URI**.
   - Enter: `http://localhost:5000/api/setup/youtube/callback` (or `http://127.0.0.1:5000/api/setup/youtube/callback`)
   - *If you are using a tunnel (like ngrok), also add that URL + `/api/setup/youtube/callback`*
7. Click **Create**.
8. You will see a popup with your **Client ID** and **Client Secret**. Keep this open!


## ⚙️ Step 5: Configure AI Chaos
1. Open your AI Chaos Setup page: [http://localhost:5000/setup](http://localhost:5000/setup)
2. Scroll down to the **YouTube Integration** section.
3. Copy the **Client ID** from Google Cloud and paste it into the **Client ID** field.
4. Copy the **Client Secret** from Google Cloud and paste it into the **Client Secret** field.
5. Click **💾 Save Credentials**.
6. Click **🔗 Connect YouTube Account**.
7. A Google login popup will appear. Select your account (the one you added as a Test User).
8. You might see a "Google hasn't verified this app" warning. Click **Advanced** > **Go to AI Chaos (unsafe)**.
9. Click **Continue** to grant access.

## 📡 Step 6: Start Listening
1. Start a YouTube Live Stream (or schedule one).
2. Copy the **Video ID** from your stream URL.
   - Example: `https://www.youtube.com/watch?v=dQw4w9WgXcQ` -> Video ID is `dQw4w9WgXcQ`
3. In the AI Chaos Setup page, paste the **Video ID**.
4. Click **▶️ Start Listening**.

✅ **Success!** You should now see "Listening" status. Chat messages starting with `!chaos` (or your configured command) will trigger events in GMod!
