#import <CoreTelephony/CTTelephonyNetworkInfo.h>

extern "C" int HudCellularGeneration()
{
    static CTTelephonyNetworkInfo *info = [[CTTelephonyNetworkInfo alloc] init];
    if (@available(iOS 13.0, *)) {
        NSString *service = info.dataServiceIdentifier;
        NSString *radio = service ? info.serviceCurrentRadioAccessTechnology[service] : nil;
        if (@available(iOS 14.1, *)) {
            if ([radio isEqualToString:CTRadioAccessTechnologyNR] ||
                [radio isEqualToString:CTRadioAccessTechnologyNRNSA]) return 5;
        }
        if ([radio isEqualToString:CTRadioAccessTechnologyLTE]) return 4;
    }
    return 0;
}
