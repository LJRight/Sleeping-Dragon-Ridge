package com.neoridge.auth;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.ByteBuffer;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public final class GuestStore {
    private static final String ALIAS = "game.guest.key.v1";

    private static SecretKey key(boolean create) throws Exception {
        KeyStore keys = KeyStore.getInstance("AndroidKeyStore");
        keys.load(null);
        if (keys.containsAlias(ALIAS)) return (SecretKey) keys.getKey(ALIAS, null);
        if (!create) throw new java.io.IOException("Guest key missing");
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return generator.generateKey();
    }

    private static AtomicFile file(Context context) {
        return new AtomicFile(new File(context.getNoBackupFilesDir(), "guest.session"));
    }

    public static synchronized String read(Context context) throws Exception {
        AtomicFile file = file(context);
        byte[] data;
        try { data = file.readFully(); }
        catch (java.io.FileNotFoundException e) {
            if (file.getBaseFile().exists() || new File(file.getBaseFile()+".bak").exists()) throw e;
            return null;
        }
        if (data.length <= 28 || data.length > 512) throw new java.io.IOException("Invalid credential");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(false), new GCMParameterSpec(128, data, 0, 12));
        return new String(cipher.doFinal(data, 12, data.length - 12), StandardCharsets.UTF_8);
    }

    public static synchronized void write(Context context, String value) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key(true));
        byte[] iv = cipher.getIV(), encrypted = cipher.doFinal(value.getBytes(StandardCharsets.UTF_8));
        if (iv.length != 12) throw new java.io.IOException("Invalid nonce");
        AtomicFile file = file(context);
        FileOutputStream stream = file.startWrite();
        try {
            stream.write(ByteBuffer.allocate(12 + encrypted.length).put(iv).put(encrypted).array());
            file.finishWrite(stream);
        } catch (Exception e) { file.failWrite(stream); throw e; }
    }
}
