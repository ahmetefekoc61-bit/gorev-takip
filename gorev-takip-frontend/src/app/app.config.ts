import { ApplicationConfig, LOCALE_ID } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localeTr from '@angular/common/locales/tr';
import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth-interceptor';

// Arayüz Türkçe ama LOCALE_ID ayarlanmadığı için | date borusu tarihleri İngilizce
// basıyordu ("30 Aug" gibi). Türkçe yerel ayarı kaydedip varsayılan olarak tanımlıyoruz.
registerLocaleData(localeTr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    { provide: LOCALE_ID, useValue: 'tr' }
  ]
};
