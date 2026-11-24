#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <string.h>

static NSDictionary *NeoGuestQuery(void) {
    return @{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
             (__bridge id)kSecAttrService:[[NSBundle mainBundle] bundleIdentifier],
             (__bridge id)kSecAttrAccount:@"game.guest.v1"};
}

extern "C" int NeoGuestRead(char *buffer, int capacity) {
    NSMutableDictionary *query = [NeoGuestQuery() mutableCopy];
    query[(__bridge id)kSecReturnData] = @YES;
    CFTypeRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (status == errSecItemNotFound) return 0;
    if (status != errSecSuccess || result == NULL) return -1;
    NSData *bytes = (__bridge NSData *)result;
    bool valid = bytes.length > 0 && bytes.length < (NSUInteger)capacity;
    if (valid) {
        memcpy(buffer, bytes.bytes, bytes.length);
        buffer[bytes.length] = 0;
    }
    CFRelease(result);
    return valid ? 1 : -1;
}

extern "C" int NeoGuestWrite(const char *value) {
    NSData *data = [NSData dataWithBytes:value length:strlen(value)];
    NSDictionary *query = NeoGuestQuery();
    NSDictionary *attributes = @{(__bridge id)kSecValueData:data};
    OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)attributes);
    if (status != errSecItemNotFound) return (int)status;
    NSMutableDictionary *entry = [query mutableCopy];
    [entry addEntriesFromDictionary:attributes];
    entry[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
    return (int)SecItemAdd((__bridge CFDictionaryRef)entry, NULL);
}
