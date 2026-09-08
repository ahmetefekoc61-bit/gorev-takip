import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth';

@Component({
  selector: 'app-login',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.css'
})
export class Login implements OnInit {
  email = '';
  password = '';
  errorMessage = '';
  infoMessage = '';
  showPassword = false;
  loading = false;
  rememberMe = false;

  constructor(
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    // Interceptor, token'ın süresi dolduğunda buraya ?expired=1 ile yönlendiriyor.
    // Kullanıcı neden giriş ekranına düştüğünü bilsin.
    if (this.route.snapshot.queryParamMap.get('expired') === '1') {
      this.infoMessage = 'Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.';
    }
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.infoMessage = '';
    this.loading = true;

    // "Beni hatırla" seçimi artık gerçekten kullanılıyor: işaretliyse token kalıcı,
    // değilse yalnızca sekme açık kaldığı sürece saklanıyor.
    this.authService.login({ email: this.email, password: this.password }, this.rememberMe).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/board']);
      },
      error: (err) => {
        this.loading = false;

        // Her hatayı "şifren yanlış" diye göstermek yanlış yönlendiriyordu: sunucu kapalıyken
        // de aynı mesaj çıkıyor ve kullanıcı doğru şifreyi defalarca deniyordu.
        // status 0 = istek sunucuya hiç ulaşamadı (bağlantı reddedildi, ağ yok, CORS).
        if (err?.status === 0) {
          this.errorMessage = 'Sunucuya ulaşılamıyor. API çalışıyor mu kontrol edin.';
        } else if (err?.status === 401) {
          this.errorMessage = 'E-posta veya şifre hatalı.';
        } else {
          this.errorMessage = 'Giriş yapılamadı, lütfen daha sonra tekrar deneyin.';
        }

        // Proje zoneless çalışıyor (zone.js yok); subscribe callback'i içindeki
        // değişiklikler otomatik yeniden render tetiklemiyor, elle bildiriyoruz.
        this.cdr.markForCheck();
      }
    });
  }
}
